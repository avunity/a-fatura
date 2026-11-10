using System.IO.Compression;
using System.Text;
using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using AFatura.Api.Features.Classification;
using AFatura.Api.Features.EFatura;
using AFatura.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Invoices;

public record InvoiceDto(
    Guid Id, Guid OrganizationId, string Status, string Source, string ExtractionMethod,
    string? SupplierNif, string? SupplierName, string? BuyerNif, string? DocumentType, string? DocumentNumber, string? Atcud,
    DateOnly? IssueDate, decimal? NetAmount, decimal? VatAmount, decimal? TotalAmount, string Currency, List<VatLine> VatLines,
    string? Summary, string? LicensePlateDetected, decimal? FuelLiters, decimal? EnergyKwh,
    Guid? CategoryId, string? CategoryName, string? CategoryColor, Guid? VehicleId, string? VehiclePlate,
    Guid? ProjectId, string? ProjectName, string? ProjectColor,
    decimal? ClassificationConfidence, string? ClassificationReason,
    bool CollectedInEFatura, Guid? EFaturaRecordId, Guid? DuplicateOfId, bool HasValidQr, List<string> Flags, string? ProcessingError,
    bool PaidPersonally, string Reimbursement, string? Notes, List<string> Tags,
    Guid UploadedByUserId, string UploadedByName, string? FileName, string? FileContentType,
    DateTime CreatedAt, DateTime? ProcessedAt, DateTime? ReviewedAt)
{
    public static InvoiceDto From(Invoice i) => new(
        i.Id, i.OrganizationId, i.Status.ToString(), i.Source.ToString(), i.ExtractionMethod.ToString(),
        i.SupplierNif, i.SupplierName, i.BuyerNif, i.DocumentType, i.DocumentNumber, i.Atcud,
        i.IssueDate, i.NetAmount, i.VatAmount, i.TotalAmount, i.Currency, i.VatLines,
        i.Summary, i.LicensePlateDetected, i.FuelLiters, i.EnergyKwh,
        i.CategoryId, i.Category?.Name, i.Category?.Color, i.VehicleId, i.Vehicle?.Plate,
        i.ProjectId, i.Project?.Name, i.Project?.Color,
        i.ClassificationConfidence, i.ClassificationReason,
        i.EFaturaRecordId != null, i.EFaturaRecordId, i.DuplicateOfId, i.HasValidQr, i.Flags, i.ProcessingError,
        i.PaidPersonally, i.Reimbursement.ToString(), i.Notes, i.Tags,
        i.UploadedByUserId, i.UploadedBy?.Name ?? "", i.File?.FileName, i.File?.ContentType,
        i.CreatedAt, i.ProcessedAt, i.ReviewedAt);
}

public record InvoiceUpdate(
    string? SupplierNif, string? SupplierName, string? BuyerNif, string? DocumentType, string? DocumentNumber, string? Atcud,
    DateOnly? IssueDate, decimal? NetAmount, decimal? VatAmount, decimal? TotalAmount,
    Guid? CategoryId, Guid? VehicleId, bool ClearVehicle, string? Notes, List<string>? Tags, bool? PaidPersonally,
    Guid? ProjectId = null, bool ClearProject = false);

public record InvoiceQuery(int? Year, int? Month, Guid? CategoryId, Guid? VehicleId, Guid? ProjectId, string? Status, Guid? UploadedBy,
    bool? Collected, string? Source, string? Q, bool? Flagged, int Page = 1, int PageSize = 50);

public static class InvoiceEndpoints
{
    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orgs/{orgId:guid}/invoices").RequireAuthorization().WithTags("Invoices");

        g.MapGet("", async (Guid orgId, [AsParameters] InvoiceQuery q, AccessService access, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var query = Filter(access.Invoices(a), q);
            var total = await query.CountAsync(ct);
            var sums = await query.GroupBy(_ => 1).Select(x => new
            {
                Total = x.Sum(i => i.TotalAmount ?? 0),
                Vat = x.Sum(i => i.VatAmount ?? 0),
                Net = x.Sum(i => i.NetAmount ?? 0)
            }).FirstOrDefaultAsync(ct);
            var pageSize = Math.Clamp(q.PageSize, 1, 500);
            var items = await query.Include(i => i.Category).Include(i => i.Vehicle).Include(i => i.Project).Include(i => i.UploadedBy).Include(i => i.File)
                .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.CreatedAt)
                .Skip((Math.Max(q.Page, 1) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new { total, sums, items = items.Select(InvoiceDto.From) });
        });

        g.MapPost("", async (Guid orgId, HttpRequest req, AccessService access, CurrentUser me, InvoiceIntake intake, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            if (!a.CanUpload) throw new AppException(403, "Sem permissão para carregar documentos");
            var form = await req.ReadFormAsync(ct);
            if (form.Files.Count == 0) throw new AppException(400, "Nenhum ficheiro enviado");

            var source = Enum.TryParse<InvoiceSource>(form["source"], true, out var s) ? s : InvoiceSource.Upload;
            Guid? vehicleId = Guid.TryParse(form["vehicleId"], out var v) ? v : null;
            Guid? categoryId = Guid.TryParse(form["categoryId"], out var c) ? c : null;
            Guid? projectId = Guid.TryParse(form["projectId"], out var pj) ? pj : null;
            if (projectId is { } pid && !await db.Projects.AnyAsync(p => p.Id == pid && p.OrganizationId == orgId && p.IsActive, ct))
                throw new AppException(400, "Projeto inválido");
            var paidPersonally = bool.TryParse(form["paidPersonally"], out var pp) && pp;

            var created = new List<object>();
            foreach (var f in form.Files)
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                var inv = await intake.CreateAsync(orgId, me.Id, source, f.FileName, f.ContentType, ms.ToArray(),
                    form["notes"], vehicleId, categoryId, paidPersonally, projectId, ct);
                created.Add(new { inv.Id, f.FileName, Status = inv.Status.ToString(), inv.CreatedAt });
            }
            return Results.Ok(created);
        }).DisableAntiforgery();

        g.MapGet("{id:guid}", async (Guid orgId, Guid id, AccessService access, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var inv = await access.Invoices(a).Include(i => i.Category).Include(i => i.Vehicle).Include(i => i.Project).Include(i => i.UploadedBy).Include(i => i.File)
                .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new AppException(404, "Fatura não encontrada");
            return Results.Ok(InvoiceDto.From(inv));
        });

        g.MapGet("{id:guid}/file", async (Guid orgId, Guid id, AccessService access, FileStorage storage, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var file = await access.Invoices(a).Where(i => i.Id == id).Select(i => i.File).FirstOrDefaultAsync(ct)
                       ?? throw new AppException(404, "Ficheiro não encontrado");
            return Results.File(storage.OpenRead(file), file.ContentType, file.FileName, enableRangeProcessing: true);
        });

        g.MapPut("{id:guid}", async (Guid orgId, Guid id, InvoiceUpdate u, AccessService access, CurrentUser me,
            ClassificationService classifier, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var inv = await access.Invoices(a).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new AppException(404, "Fatura não encontrada");
            await access.EnsurePeriodOpenAsync(a, inv.IssueDate, ct);
            await access.EnsurePeriodOpenAsync(a, u.IssueDate, ct);

            if (u.CategoryId is { } cid && !await db.Categories.AnyAsync(c => c.Id == cid && (c.OrganizationId == null || c.OrganizationId == orgId), ct))
                throw new AppException(400, "Categoria inválida");
            if (u.VehicleId is { } vid && !await db.Vehicles.AnyAsync(v => v.Id == vid && v.OrganizationId == orgId, ct))
                throw new AppException(400, "Viatura inválida");
            if (u.ProjectId is { } pid && !await db.Projects.AnyAsync(p => p.Id == pid && p.OrganizationId == orgId, ct))
                throw new AppException(400, "Projeto inválido");

            var categoryChanged = u.CategoryId != null && u.CategoryId != inv.CategoryId;
            var vehicleChanged = u.VehicleId != null && u.VehicleId != inv.VehicleId;

            inv.SupplierNif = u.SupplierNif != null ? Text.NormalizeNif(u.SupplierNif) : inv.SupplierNif;
            inv.SupplierName = u.SupplierName ?? inv.SupplierName;
            inv.BuyerNif = u.BuyerNif != null ? Text.NormalizeNif(u.BuyerNif) : inv.BuyerNif;
            inv.DocumentType = u.DocumentType ?? inv.DocumentType;
            inv.DocumentNumber = u.DocumentNumber ?? inv.DocumentNumber;
            inv.Atcud = u.Atcud ?? inv.Atcud;
            inv.IssueDate = u.IssueDate ?? inv.IssueDate;
            inv.NetAmount = u.NetAmount ?? inv.NetAmount;
            inv.VatAmount = u.VatAmount ?? inv.VatAmount;
            inv.TotalAmount = u.TotalAmount ?? inv.TotalAmount;
            if (u.CategoryId != null) { inv.CategoryId = u.CategoryId; inv.ClassificationConfidence = 1; inv.ClassificationReason = "Manual"; }
            if (u.ClearVehicle) inv.VehicleId = null;
            else if (u.VehicleId != null) inv.VehicleId = u.VehicleId;
            if (u.ClearProject) inv.ProjectId = null;
            else if (u.ProjectId != null) inv.ProjectId = u.ProjectId;
            inv.Notes = u.Notes ?? inv.Notes;
            inv.Tags = u.Tags ?? inv.Tags;
            if (u.PaidPersonally is { } p && p != inv.PaidPersonally)
            {
                inv.PaidPersonally = p;
                inv.Reimbursement = p ? ReimbursementStatus.Pending : ReimbursementStatus.NotApplicable;
            }
            if (inv.ExtractionMethod == ExtractionMethod.None) inv.ExtractionMethod = ExtractionMethod.Manual;
            inv.Flags.Remove(InvoiceFlags.MissingFields);

            if (categoryChanged || vehicleChanged)
                await classifier.LearnAsync(orgId, inv.SupplierNif, categoryChanged ? inv.CategoryId : null, vehicleChanged ? inv.VehicleId : null, ct);

            audit.Log(orgId, me.Id, "invoice.update", nameof(Invoice), inv.Id);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("{id:guid}/review", async (Guid orgId, Guid id, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.RequireSeeAllAsync(orgId, ct);
            var inv = await access.Invoices(a).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new AppException(404, "Fatura não encontrada");
            inv.Status = InvoiceStatus.Processed;
            inv.ReviewedAt = DateTime.UtcNow;
            inv.ReviewedByUserId = me.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("{id:guid}/reprocess", async (Guid orgId, Guid id, AccessService access, InvoiceQueue queue, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var inv = await access.Invoices(a).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new AppException(404, "Fatura não encontrada");
            await access.EnsurePeriodOpenAsync(a, inv.IssueDate, ct);
            inv.Status = InvoiceStatus.Pending;
            inv.ProcessingError = null;
            await db.SaveChangesAsync(ct);
            await queue.EnqueueAsync(inv.Id);
            return Results.Accepted();
        });

        g.MapPost("{id:guid}/reimbursement", async (Guid orgId, Guid id, [FromBody] ReimbursementUpdate body, AccessService access,
            CurrentUser me, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.RequireManageAsync(orgId, ct);
            var inv = await access.Invoices(a).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new AppException(404, "Fatura não encontrada");
            inv.Reimbursement = body.Status;
            audit.Log(orgId, me.Id, "invoice.reimbursement", nameof(Invoice), inv.Id, body.Status.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("{id:guid}", async (Guid orgId, Guid id, AccessService access, CurrentUser me, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var inv = await access.Invoices(a).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new AppException(404, "Fatura não encontrada");
            if (!a.CanManage && inv.UploadedByUserId != me.Id) throw new AppException(403, "Só pode apagar documentos carregados por si");
            await access.EnsurePeriodOpenAsync(a, inv.IssueDate, ct);
            await db.EFaturaRecords.Where(r => r.MatchedInvoiceId == inv.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.MatchedInvoiceId, (Guid?)null), ct);
            await db.BankTransactions.Where(t => t.MatchedInvoiceId == inv.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.MatchedInvoiceId, (Guid?)null), ct);
            db.Invoices.Remove(inv);
            audit.Log(orgId, me.Id, "invoice.delete", nameof(Invoice), inv.Id, $"{inv.SupplierNif} {inv.DocumentNumber}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("summary", async (Guid orgId, int year, int? month, AccessService access, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var yearQ = access.Invoices(a).Where(i => i.IssueDate != null && i.IssueDate.Value.Year == year && i.DuplicateOfId == null);
            var byMonth = await yearQ.GroupBy(i => i.IssueDate!.Value.Month)
                .Select(x => new { Month = x.Key, Total = x.Sum(i => i.TotalAmount ?? 0), Vat = x.Sum(i => i.VatAmount ?? 0), Count = x.Count() })
                .OrderBy(x => x.Month).ToListAsync(ct);

            var periodQ = month is { } m ? yearQ.Where(i => i.IssueDate!.Value.Month == m) : yearQ;
            var byCategory = await periodQ.GroupBy(i => new { i.CategoryId, Name = i.Category != null ? i.Category.Name : null, Color = i.Category != null ? i.Category.Color : null })
                .Select(x => new { x.Key.CategoryId, Name = x.Key.Name ?? "Sem categoria", Color = x.Key.Color ?? "#cbd5e1", Total = x.Sum(i => i.TotalAmount ?? 0), Count = x.Count() })
                .OrderByDescending(x => x.Total).ToListAsync(ct);
            var byVehicle = await periodQ.Where(i => i.VehicleId != null)
                .GroupBy(i => new { i.VehicleId, i.Vehicle!.Plate })
                .Select(x => new { x.Key.VehicleId, x.Key.Plate, Total = x.Sum(i => i.TotalAmount ?? 0), Count = x.Count() })
                .OrderByDescending(x => x.Total).ToListAsync(ct);
            var byProject = await periodQ.Where(i => i.ProjectId != null)
                .GroupBy(i => new { i.ProjectId, i.Project!.Name, i.Project.Color })
                .Select(x => new { x.Key.ProjectId, x.Key.Name, x.Key.Color, Total = x.Sum(i => i.TotalAmount ?? 0), Count = x.Count() })
                .OrderByDescending(x => x.Total).ToListAsync(ct);
            var byStatus = await periodQ.GroupBy(i => i.Status).Select(x => new { Status = x.Key.ToString(), Count = x.Count() }).ToListAsync(ct);
            var collected = await periodQ.CountAsync(i => i.EFaturaRecordId != null, ct);
            var count = await periodQ.CountAsync(ct);
            var pendingReimbursement = await periodQ.Where(i => i.Reimbursement == ReimbursementStatus.Pending || i.Reimbursement == ReimbursementStatus.Approved)
                .SumAsync(i => i.TotalAmount ?? 0, ct);

            return Results.Ok(new { byMonth, byCategory, byVehicle, byProject, byStatus, count, collected, pendingReimbursement });
        });

        g.MapGet("export", async (Guid orgId, int year, int month, AccessService access, FileStorage storage, CancellationToken ct) =>
        {
            var a = await access.RequireSeeAllAsync(orgId, ct);
            var invoices = await access.Invoices(a).Include(i => i.File).Include(i => i.Category).Include(i => i.Vehicle).Include(i => i.Project).Include(i => i.UploadedBy)
                .Where(i => i.IssueDate != null && i.IssueDate.Value.Year == year && i.IssueDate.Value.Month == month && i.DuplicateOfId == null)
                .OrderBy(i => i.IssueDate).ToListAsync(ct);

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var csv = new StringBuilder("Data;NIF Emitente;Emitente;Tipo;Número;ATCUD;Base;IVA;Total;Categoria;Viatura;Projeto;e-Fatura;Carregado por;Ficheiro\n");
                foreach (var i in invoices)
                {
                    var name = i.File == null ? "" :
                        $"{i.IssueDate:yyyy-MM-dd}_{Text.NormalizeNif(i.SupplierNif)}_{Text.NormalizeDocNumber(i.DocumentNumber).Replace('/', '-')}_{i.Id.ToString()[..8]}{Path.GetExtension(i.File.FileName)}";
                    if (i.File != null && File.Exists(storage.FullPath(i.File)))
                        zip.CreateEntryFromFile(storage.FullPath(i.File), $"documentos/{name}");
                    csv.AppendLine(string.Join(';', new[]
                    {
                        i.IssueDate?.ToString("yyyy-MM-dd"), i.SupplierNif, i.SupplierName, i.DocumentType, i.DocumentNumber, i.Atcud,
                        i.NetAmount?.ToString("0.00"), i.VatAmount?.ToString("0.00"), i.TotalAmount?.ToString("0.00"),
                        i.Category?.Name, i.Vehicle?.Plate, i.Project?.Name, i.EFaturaRecordId != null ? "Sim" : "Não", i.UploadedBy?.Name, name
                    }.Select(Csv.Escape)));
                }
                var entry = zip.CreateEntry("resumo.csv");
                await using var w = new StreamWriter(entry.Open(), new UTF8Encoding(true));
                await w.WriteAsync(csv.ToString());
            }
            ms.Position = 0;
            return Results.File(ms, "application/zip", $"a-fatura_{year}-{month:00}.zip");
        });
    }

    public record ReimbursementUpdate(ReimbursementStatus Status);

    private static IQueryable<Invoice> Filter(IQueryable<Invoice> q, InvoiceQuery f)
    {
        // Documents still without an issue date (just uploaded / being processed) belong to the period of their upload,
        // so they show up in the list straight away.
        if (f.Year is { } y) q = q.Where(i => i.IssueDate != null ? i.IssueDate.Value.Year == y : i.CreatedAt.Year == y);
        if (f.Month is { } m) q = q.Where(i => i.IssueDate != null ? i.IssueDate.Value.Month == m : i.CreatedAt.Month == m);
        if (f.CategoryId is { } c) q = q.Where(i => i.CategoryId == c);
        if (f.VehicleId is { } v) q = q.Where(i => i.VehicleId == v);
        if (f.ProjectId is { } pr) q = q.Where(i => i.ProjectId == pr);
        if (f.UploadedBy is { } u) q = q.Where(i => i.UploadedByUserId == u);
        if (f.Collected is { } col) q = col ? q.Where(i => i.EFaturaRecordId != null) : q.Where(i => i.EFaturaRecordId == null);
        if (Enum.TryParse<InvoiceStatus>(f.Status, true, out var st)) q = q.Where(i => i.Status == st);
        if (Enum.TryParse<InvoiceSource>(f.Source, true, out var src)) q = q.Where(i => i.Source == src);
        if (f.Flagged == true) q = q.Where(i => i.Flags.Count > 0);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = $"%{f.Q.Trim()}%";
            q = q.Where(i => EF.Functions.ILike(i.SupplierName ?? "", term) || EF.Functions.ILike(i.DocumentNumber ?? "", term)
                             || EF.Functions.ILike(i.SupplierNif ?? "", term) || EF.Functions.ILike(i.Summary ?? "", term));
        }
        return q;
    }
}
