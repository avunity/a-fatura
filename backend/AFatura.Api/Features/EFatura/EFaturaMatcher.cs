using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.EFatura;

/// <summary>
/// Links uploaded documents with the records communicated to AT (e-Fatura).
/// Match priority: same supplier NIF and (same document number | same ATCUD | same date and total).
/// </summary>
public class EFaturaMatcher(AppDbContext db)
{
    public static string Key(string? nif, string? docNumber) => $"{Text.NormalizeNif(nif)}|{Text.NormalizeDocNumber(docNumber)}";

    public async Task<EFaturaRecord?> FindForInvoiceAsync(Invoice inv, CancellationToken ct = default)
    {
        var nif = Text.NormalizeNif(inv.SupplierNif);
        if (nif.Length == 0) return null;
        var candidates = await db.EFaturaRecords
            .Where(r => r.OrganizationId == inv.OrganizationId && r.SupplierNif == nif
                        && (r.MatchedInvoiceId == null || r.MatchedInvoiceId == inv.Id))
            .ToListAsync(ct);
        return Best(inv, candidates);
    }

    public static EFaturaRecord? Best(Invoice inv, IEnumerable<EFaturaRecord> candidates)
    {
        var doc = Text.NormalizeDocNumber(inv.DocumentNumber);
        var atcud = Text.NormalizeDocNumber(inv.Atcud);
        EFaturaRecord? fallback = null;
        foreach (var r in candidates)
        {
            if (doc.Length > 0 && Text.NormalizeDocNumber(r.DocumentNumber) == doc) return r;
            if (atcud.Length > 0 && Text.NormalizeDocNumber(r.Atcud) == atcud) return r;
            if (fallback == null && inv.IssueDate == r.IssueDate && inv.TotalAmount is { } t && Math.Abs(t - r.Total) <= 0.01m)
                fallback = r;
        }
        return fallback;
    }

    public async Task<bool> TryLinkAsync(Invoice inv, CancellationToken ct = default)
    {
        var rec = await FindForInvoiceAsync(inv, ct);
        if (rec == null) return false;
        rec.MatchedInvoiceId = inv.Id;
        inv.EFaturaRecordId = rec.Id;
        await FulfillRequestsAsync(rec.Id, inv.Id, ct);
        return true;
    }

    /// <summary>After an e-Fatura import, link records to documents already uploaded. Returns number of links made.</summary>
    public async Task<int> MatchRecordsAsync(Guid orgId, IReadOnlyCollection<EFaturaRecord> records, CancellationToken ct = default)
    {
        var nifs = records.Where(r => r.MatchedInvoiceId == null).Select(r => r.SupplierNif).Distinct().ToList();
        if (nifs.Count == 0) return 0;
        var invoices = await db.Invoices
            .Where(i => i.OrganizationId == orgId && i.EFaturaRecordId == null && i.DuplicateOfId == null
                        && i.SupplierNif != null && nifs.Contains(i.SupplierNif))
            .ToListAsync(ct);

        var linked = 0;
        foreach (var inv in invoices)
        {
            var rec = Best(inv, records.Where(r => r.MatchedInvoiceId == null && r.SupplierNif == Text.NormalizeNif(inv.SupplierNif)));
            if (rec == null) continue;
            rec.MatchedInvoiceId = inv.Id;
            inv.EFaturaRecordId = rec.Id;
            inv.Flags.Remove(InvoiceFlags.NotInEFatura);
            linked++;
        }
        return linked;
    }

    private async Task FulfillRequestsAsync(Guid recordId, Guid invoiceId, CancellationToken ct)
    {
        var open = await db.DocumentRequests
            .Where(d => d.EFaturaRecordId == recordId && d.Status == DocumentRequestStatus.Open).ToListAsync(ct);
        foreach (var d in open)
        {
            d.Status = DocumentRequestStatus.Fulfilled;
            d.FulfilledByInvoiceId = invoiceId;
            d.ResolvedAt = DateTime.UtcNow;
        }
    }
}

public static class InvoiceFlags
{
    public const string BuyerNifMismatch = "NIF do adquirente diferente do NIF da entidade";
    public const string NoBuyerNif = "Fatura sem NIF do adquirente";
    public const string InvalidSupplierNif = "NIF do emitente inválido";
    public const string MissingAtcud = "Sem ATCUD";
    public const string TotalsMismatch = "Base + IVA não corresponde ao total";
    public const string Duplicate = "Possível duplicado";
    public const string NotInEFatura = "Ainda não consta no e-Fatura importado";
    public const string LowConfidence = "Extração com baixa confiança — rever";
    public const string NotAnInvoice = "O documento não parece ser uma fatura";
    public const string MissingFields = "Faltam dados essenciais (NIF, data ou total)";
}
