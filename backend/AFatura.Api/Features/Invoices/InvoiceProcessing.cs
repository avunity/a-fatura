using System.Text.Json;
using System.Threading.Channels;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using AFatura.Api.Features.Classification;
using AFatura.Api.Features.EFatura;
using AFatura.Api.Features.Extraction;
using AFatura.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Invoices;

/// <summary>In-memory queue of invoice ids to process. Pending invoices are re-queued at startup, so nothing is lost on restart.</summary>
public class InvoiceQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public ValueTask EnqueueAsync(Guid id) => _channel.Writer.WriteAsync(id);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public class InvoiceWorker(InvoiceQueue queue, IServiceScopeFactory scopes, ILogger<InvoiceWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.Invoices
                .Where(i => i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.Processing)
                .Select(i => i.Id).ToListAsync(stoppingToken);
            foreach (var id in pending) await queue.EnqueueAsync(id);
            if (pending.Count > 0) log.LogInformation("Re-queued {Count} pending invoices", pending.Count);
        }

        // Two parallel consumers: QR decoding is CPU bound, AI calls are I/O bound.
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => ConsumeAsync(stoppingToken)));
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var id in queue.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<InvoiceProcessor>().ProcessAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed processing invoice {Id}", id);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var inv = await db.Invoices.FindAsync([id], ct);
                if (inv != null)
                {
                    inv.Status = InvoiceStatus.Failed;
                    inv.ProcessingError = ex.Message;
                    await db.SaveChangesAsync(ct);
                }
            }
        }
    }
}

/// <summary>
/// Pipeline: AT QR code (authoritative fiscal data) → AI (supplier name, plate, category, and fallback for the fiscal fields)
/// → duplicate check → category/vehicle → e-Fatura match → validation flags.
/// </summary>
public class InvoiceProcessor(
    AppDbContext db,
    FileStorage storage,
    QrDecoder qr,
    IAiExtractor ai,
    ClassificationService classifier,
    EFaturaMatcher efatura,
    ILogger<InvoiceProcessor> log)
{
    private const decimal ReviewThreshold = 0.6m;

    public async Task ProcessAsync(Guid invoiceId, CancellationToken ct)
    {
        var inv = await db.Invoices.Include(i => i.File).Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (inv == null || inv.File == null) return;

        inv.Status = InvoiceStatus.Processing;
        await db.SaveChangesAsync(ct);

        var data = await storage.ReadAsync(inv.File, ct);
        var categories = await classifier.CategoriesForAsync(inv.OrganizationId, ct);

        // 1) Fiscal QR code
        var qrTexts = qr.Decode(data, inv.File.ContentType);
        var atQr = qrTexts.Select(AtQrCodeParser.TryParse).FirstOrDefault(q => q != null);
        if (atQr != null) ApplyQr(inv, atQr);

        var supplier = await FindSupplierAsync(inv.OrganizationId, inv.SupplierNif, ct);

        // 2) AI — skipped when the QR already gives everything and the supplier is known (saves AI calls)
        AiExtraction? aiResult = null;
        var needAi = ai.Enabled && (atQr == null || supplier?.DefaultCategoryId == null || string.IsNullOrEmpty(supplier.Name));
        if (needAi)
        {
            try
            {
                var (result, raw) = await ai.ExtractAsync(storage.FullPath(inv.File), inv.File.ContentType, data,
                    categories.Select(c => c.Name).ToList(), ct);
                aiResult = result;
                inv.ExtractionJson = raw?.Length > 20000 ? raw[..20000] : raw;
                if (aiResult != null) ApplyAi(inv, aiResult, overwriteFiscal: atQr == null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "AI extraction failed for {Id}", inv.Id);
                inv.ProcessingError = "IA indisponível: " + ex.Message;
            }
        }

        inv.ExtractionMethod = (atQr, aiResult) switch
        {
            (not null, not null) => ExtractionMethod.QrAndAi,
            (not null, null) => ExtractionMethod.Qr,
            (null, not null) => ExtractionMethod.Ai,
            _ => ExtractionMethod.None
        };
        inv.SupplierNif = Text.NormalizeNif(inv.SupplierNif) is { Length: > 0 } n ? n : null;
        inv.BuyerNif = Text.NormalizeNif(inv.BuyerNif) is { Length: > 0 } b ? b : null;
        supplier ??= await FindSupplierAsync(inv.OrganizationId, inv.SupplierNif, ct);
        if (supplier != null && string.IsNullOrEmpty(inv.SupplierName)) inv.SupplierName = supplier.Name;

        // 3) Duplicates (same supplier + doc number, or same ATCUD, or same file hash)
        inv.Flags.Clear();
        var dup = await FindDuplicateAsync(inv, ct);
        if (dup != null) { inv.DuplicateOfId = dup; inv.Flags.Add(InvoiceFlags.Duplicate); }

        // 4) Classification (keep manual choices if already set)
        if (inv.CategoryId == null)
        {
            var guess = classifier.GuessCategory(categories, supplier, aiResult?.Category, aiResult?.CategoryConfidence,
                inv.SupplierName, inv.Summary);
            inv.CategoryId = guess.CategoryId;
            inv.ClassificationConfidence = guess.Confidence;
            inv.ClassificationReason = guess.Reason;
        }
        if (inv.VehicleId == null)
        {
            var category = categories.FirstOrDefault(c => c.Id == inv.CategoryId);
            var vg = await classifier.GuessVehicleAsync(inv.OrganizationId, inv.LicensePlateDetected, supplier, category, ct);
            if (vg.VehicleId != null)
            {
                inv.VehicleId = vg.VehicleId;
                inv.ClassificationReason = $"{inv.ClassificationReason}; {vg.Reason}";
            }
        }

        // 5) e-Fatura
        if (inv.DuplicateOfId == null && inv.EFaturaRecordId == null)
        {
            var linked = await efatura.TryLinkAsync(inv, ct);
            if (!linked && inv.IssueDate is { } d && await db.EFaturaImports.AnyAsync(
                    x => x.OrganizationId == inv.OrganizationId && x.PeriodStart <= d && x.PeriodEnd >= d, ct))
                inv.Flags.Add(InvoiceFlags.NotInEFatura);
        }

        // 6) Validation
        Validate(inv, aiResult);
        await UpsertSupplierAsync(inv, supplier, ct);

        inv.Status = inv.Flags.Count == 0 && (inv.ClassificationConfidence ?? 0) >= ReviewThreshold
            ? InvoiceStatus.Processed : InvoiceStatus.NeedsReview;
        inv.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Invoice {Id} processed via {Method}: {Status}", inv.Id, inv.ExtractionMethod, inv.Status);
    }

    private static void ApplyQr(Invoice inv, AtQrCode q)
    {
        inv.HasValidQr = true;
        inv.QrRaw = q.Raw;
        inv.SupplierNif = q.SupplierNif;
        inv.BuyerNif = q.BuyerNif;
        inv.DocumentType = q.DocumentType;
        inv.DocumentNumber = q.DocumentNumber;
        inv.Atcud = q.Atcud;
        inv.IssueDate = q.IssueDate;
        inv.VatLines = q.VatLines;
        inv.VatAmount = q.TotalTaxes ?? q.VatLines.Sum(v => v.Vat);
        inv.TotalAmount = q.Total;
        inv.NetAmount = q.Total is { } t ? t - (q.TotalTaxes ?? 0) : q.NetAmount;
    }

    private static void ApplyAi(Invoice inv, AiExtraction a, bool overwriteFiscal)
    {
        inv.SupplierName ??= a.SupplierName?.Trim();
        inv.Summary ??= a.Summary;
        inv.LicensePlateDetected ??= a.LicensePlate;
        inv.FuelLiters ??= a.FuelLiters;
        inv.EnergyKwh ??= a.EnergyKwh;
        if (!string.IsNullOrWhiteSpace(a.Currency)) inv.Currency = a.Currency.ToUpperInvariant();
        if (!overwriteFiscal) return;

        inv.SupplierNif ??= a.SupplierNif;
        inv.BuyerNif ??= a.BuyerNif;
        inv.DocumentType ??= a.DocumentType;
        inv.DocumentNumber ??= a.DocumentNumber;
        inv.Atcud ??= a.Atcud;
        inv.IssueDate ??= Text.ParseDate(a.IssueDate);
        inv.NetAmount ??= a.NetAmount;
        inv.VatAmount ??= a.VatAmount;
        inv.TotalAmount ??= a.TotalAmount;
        if (inv.VatLines.Count == 0 && a.VatLines != null)
            inv.VatLines = a.VatLines.Select(v => new VatLine { Region = "PT", Rate = v.Rate, Base = v.Base, Vat = v.Vat }).ToList();
    }

    private void Validate(Invoice inv, AiExtraction? ai)
    {
        if (ai?.IsInvoice == false && !inv.HasValidQr) inv.Flags.Add(InvoiceFlags.NotAnInvoice);
        if (inv.SupplierNif == null || inv.IssueDate == null || inv.TotalAmount == null) inv.Flags.Add(InvoiceFlags.MissingFields);
        if (inv.SupplierNif != null && inv.SupplierNif.Length == 9 && inv.SupplierNif.All(char.IsDigit) && !Text.IsValidPtNif(inv.SupplierNif))
            inv.Flags.Add(InvoiceFlags.InvalidSupplierNif);

        var orgNif = Text.NormalizeNif(inv.Organization.Nif);
        if (orgNif.Length > 0)
        {
            if (inv.BuyerNif == null) inv.Flags.Add(InvoiceFlags.NoBuyerNif);
            else if (inv.BuyerNif != orgNif) inv.Flags.Add(InvoiceFlags.BuyerNifMismatch);
        }

        if (inv.IssueDate is { Year: >= 2023 } && string.IsNullOrEmpty(inv.Atcud) && inv.Currency == "EUR"
            && inv.SupplierNif is { Length: 9 })
            inv.Flags.Add(InvoiceFlags.MissingAtcud);

        if (inv is { NetAmount: { } net, VatAmount: { } vat, TotalAmount: { } total } && Math.Abs(net + vat - total) > 0.05m)
            inv.Flags.Add(InvoiceFlags.TotalsMismatch);

        if (!inv.HasValidQr && (ai?.Confidence ?? 0) < ReviewThreshold) inv.Flags.Add(InvoiceFlags.LowConfidence);
    }

    private async Task<Guid?> FindDuplicateAsync(Invoice inv, CancellationToken ct)
    {
        var q = db.Invoices.Where(i => i.OrganizationId == inv.OrganizationId && i.Id != inv.Id && i.DuplicateOfId == null);
        var doc = inv.DocumentNumber;
        if (inv.SupplierNif != null && !string.IsNullOrEmpty(doc))
        {
            var norm = Text.NormalizeDocNumber(doc);
            var sameSupplier = await q.Where(i => i.SupplierNif == inv.SupplierNif && i.DocumentNumber != null)
                .Select(i => new { i.Id, i.DocumentNumber, i.CreatedAt }).ToListAsync(ct);
            var hit = sameSupplier.Where(i => Text.NormalizeDocNumber(i.DocumentNumber) == norm)
                .OrderBy(i => i.CreatedAt).FirstOrDefault();
            if (hit != null) return hit.Id;
        }
        if (!string.IsNullOrEmpty(inv.Atcud))
        {
            var id = await q.Where(i => i.Atcud == inv.Atcud).OrderBy(i => i.CreatedAt).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
            if (id != null) return id;
        }
        if (inv.File != null)
        {
            var hash = inv.File.Sha256;
            return await q.Where(i => i.File != null && i.File.Sha256 == hash).OrderBy(i => i.CreatedAt)
                .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        }
        return null;
    }

    private Task<Supplier?> FindSupplierAsync(Guid orgId, string? nif, CancellationToken ct)
    {
        var n = Text.NormalizeNif(nif);
        return n.Length == 0
            ? Task.FromResult<Supplier?>(null)
            : db.Suppliers.FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.Nif == n, ct);
    }

    private Task UpsertSupplierAsync(Invoice inv, Supplier? supplier, CancellationToken ct)
    {
        if (inv.SupplierNif == null || inv.DuplicateOfId != null) return Task.CompletedTask;
        if (supplier == null)
        {
            supplier = new Supplier { OrganizationId = inv.OrganizationId, Nif = inv.SupplierNif };
            db.Suppliers.Add(supplier);
        }
        if (string.IsNullOrEmpty(supplier.Name) && !string.IsNullOrEmpty(inv.SupplierName)) supplier.Name = inv.SupplierName;
        supplier.InvoiceCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Creates invoice rows from incoming files (web upload, mobile, e-mail) and queues processing.</summary>
public class InvoiceIntake(AppDbContext db, FileStorage storage, InvoiceQueue queue)
{
    public async Task<Invoice> CreateAsync(Guid orgId, Guid userId, InvoiceSource source, string fileName, string? contentType,
        byte[] data, string? notes = null, Guid? vehicleId = null, Guid? categoryId = null, bool paidPersonally = false,
        Guid? projectId = null, CancellationToken ct = default)
    {
        var type = string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream"
            ? FileStorage.GuessContentType(fileName) : contentType;
        if (!FileStorage.AllowedInvoiceTypes.Contains(type))
            throw new AppException(415, $"Tipo de ficheiro não suportado: {type}");
        var file = await storage.SaveAsync(orgId, fileName, type, data, ct);

        var inv = new Invoice
        {
            OrganizationId = orgId,
            UploadedByUserId = userId,
            Source = source,
            FileId = file.Id,
            Notes = notes,
            VehicleId = vehicleId,
            CategoryId = categoryId,
            ProjectId = projectId,
            PaidPersonally = paidPersonally,
            Reimbursement = paidPersonally ? ReimbursementStatus.Pending : ReimbursementStatus.NotApplicable,
        };
        db.Invoices.Add(inv);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(inv.Id);
        return inv;
    }
}
