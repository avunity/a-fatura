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

    /// <summary>The invoices a document stands for: its sub-documents (multi-invoice files) or the document itself.</summary>
    public static List<SubDocument> Parts(Invoice inv) =>
        inv.SubDocuments is { Count: > 0 } subs
            ? subs
            : [new SubDocument { SupplierNif = Text.NormalizeNif(inv.SupplierNif), DocumentNumber = inv.DocumentNumber, Atcud = inv.Atcud, IssueDate = inv.IssueDate, Total = inv.TotalAmount }];

    public static EFaturaRecord? Best(SubDocument part, IEnumerable<EFaturaRecord> candidates)
    {
        var doc = Text.NormalizeDocNumber(part.DocumentNumber);
        var atcud = Text.NormalizeDocNumber(part.Atcud);
        EFaturaRecord? fallback = null;
        foreach (var r in candidates)
        {
            if (doc.Length > 0 && Text.NormalizeDocNumber(r.DocumentNumber) == doc) return r;
            if (atcud.Length > 0 && Text.NormalizeDocNumber(r.Atcud) == atcud) return r;
            if (fallback == null && part.IssueDate == r.IssueDate && part.Total is { } t && Math.Abs(t - r.Total) <= 0.01m)
                fallback = r;
        }
        return fallback;
    }

    /// <summary>Links every invoice in the document with its e-Fatura line. Returns true when at least one was linked.</summary>
    public async Task<bool> TryLinkAsync(Invoice inv, CancellationToken ct = default)
    {
        var parts = Parts(inv);
        var nifs = parts.Select(p => p.SupplierNif).Where(n => n.Length > 0).Distinct().ToList();
        if (nifs.Count == 0) return false;
        var candidates = await db.EFaturaRecords
            .Where(r => r.OrganizationId == inv.OrganizationId && nifs.Contains(r.SupplierNif)
                        && (r.MatchedInvoiceId == null || r.MatchedInvoiceId == inv.Id))
            .ToListAsync(ct);
        var linked = LinkParts(inv, parts, candidates);
        foreach (var rec in linked) await FulfillRequestsAsync(rec.Id, inv.Id, ct);
        return linked.Count > 0 || candidates.Any(r => r.MatchedInvoiceId == inv.Id);
    }

    private static List<EFaturaRecord> LinkParts(Invoice inv, List<SubDocument> parts, List<EFaturaRecord> candidates)
    {
        var linked = new List<EFaturaRecord>();
        foreach (var part in parts)
        {
            var rec = Best(part, candidates.Where(r => r.SupplierNif == part.SupplierNif && (r.MatchedInvoiceId == null || r.MatchedInvoiceId == inv.Id) && !linked.Contains(r)));
            if (rec == null) continue;
            rec.MatchedInvoiceId = inv.Id;
            linked.Add(rec);
        }
        if (linked.Count > 0)
        {
            inv.EFaturaRecordId ??= linked[0].Id;
            inv.Flags.Remove(InvoiceFlags.NotInEFatura);
        }
        return linked;
    }

    /// <summary>After an e-Fatura import, link records to documents already uploaded. Returns number of records linked.</summary>
    public async Task<int> MatchRecordsAsync(Guid orgId, IReadOnlyCollection<EFaturaRecord> records, CancellationToken ct = default)
    {
        var open = records.Where(r => r.MatchedInvoiceId == null).ToList();
        var nifs = open.Select(r => r.SupplierNif).Distinct().ToList();
        if (nifs.Count == 0) return 0;
        var invoices = (await db.Invoices
                .Where(i => i.OrganizationId == orgId && i.DuplicateOfId == null && i.SupplierNif != null && nifs.Contains(i.SupplierNif))
                .ToListAsync(ct))
            .Where(i => i.EFaturaRecordId == null || i.SubDocuments.Count > 0) // multi-invoice files may still have unlinked parts
            .ToList();

        var total = 0;
        foreach (var inv in invoices)
        {
            var linked = LinkParts(inv, Parts(inv), open);
            total += linked.Count;
            open.RemoveAll(linked.Contains);
            foreach (var rec in linked) await FulfillRequestsAsync(rec.Id, inv.Id, ct);
        }
        return total;
    }

    private async Task FulfillRequestsAsync(Guid recordId, Guid invoiceId, CancellationToken ct)
    {
        var openRequests = await db.DocumentRequests
            .Where(d => d.EFaturaRecordId == recordId && d.Status == DocumentRequestStatus.Open).ToListAsync(ct);
        foreach (var d in openRequests)
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
