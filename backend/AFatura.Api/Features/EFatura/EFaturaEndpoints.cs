using System.Text.RegularExpressions;
using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.EFatura;

public record EFaturaRow(string SupplierNif, string? SupplierName, string DocumentNumber, string? Atcud, string? DocumentType,
    DateOnly IssueDate, decimal Total, decimal? Vat, decimal? Base, string? Sector, string? Status);

/// <summary>
/// Parses the CSV exported from the e-Fatura portal (Faturas → Consultar → exportar). Column names vary between
/// exports and over time, so headers are matched by alias. Typical header:
/// Setor;Emitente;Nº Fatura / ATCUD;Tipo;Data Emissão;Total;IVA;Base Tributável;Situação;Comunicação Emitente;Comunicação Adquirente
/// </summary>
public static partial class EFaturaCsvParser
{
    public static (List<EFaturaRow> Rows, List<string> Errors) Parse(byte[] bytes)
    {
        var rows = Csv.Parse(Csv.Decode(bytes));
        var errors = new List<string>();
        var headerIdx = rows.FindIndex(r => r.Any(c => Text.Fold(c).Contains("emitente")) || r.Any(c => Text.Fold(c).Contains("nif")));
        if (headerIdx < 0) return (new(), new() { "Cabeçalho do ficheiro e-Fatura não reconhecido" });
        var h = rows[headerIdx];

        int cNifSeparate = Csv.Find(h, "nif emitente", "nif do emitente", "nif");
        int cEmitter = Csv.Find(h, "emitente", "nome emitente", "comerciante");
        int cDoc = Csv.Find(h, "nº fatura / atcud", "n.º fatura", "nº fatura", "numero fatura", "fatura", "documento", "numero");
        int cAtcud = Csv.Find(h, "atcud");
        int cType = Csv.Find(h, "tipo");
        int cDate = Csv.Find(h, "data emissao", "data de emissao", "data");
        int cTotal = Csv.Find(h, "total");
        int cVat = Csv.Find(h, "iva");
        int cBase = Csv.Find(h, "base tributavel", "base");
        int cSector = Csv.Find(h, "setor", "sector", "atividade");
        int cStatus = Csv.Find(h, "situacao", "estado");

        if (cDate < 0 || cTotal < 0 || (cEmitter < 0 && cNifSeparate < 0))
            return (new(), new() { "Colunas obrigatórias em falta (Emitente/NIF, Data, Total)" });

        var result = new List<EFaturaRow>();
        for (var i = headerIdx + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var emitter = Csv.At(r, cEmitter) ?? "";
            string? nif = null, name = null;
            var m = EmitterRx().Match(emitter);
            if (m.Success) { nif = m.Groups["nif"].Value; name = m.Groups["name"].Value.Trim(); }
            else
            {
                var sep = Csv.At(r, cNifSeparate);
                var nm = NifRx().Match(sep ?? emitter);
                if (nm.Success) nif = nm.Value;
                name = cEmitter >= 0 && cEmitter != cNifSeparate ? emitter : null;
            }

            var docRaw = Csv.At(r, cDoc) ?? "";
            var atcud = cAtcud >= 0 && cAtcud != cDoc ? Csv.At(r, cAtcud) : null;
            var docNumber = docRaw;
            var slash = docRaw.LastIndexOf(" / ", StringComparison.Ordinal);
            if (slash > 0 && AtcudRx().IsMatch(docRaw[(slash + 3)..].Trim()))
            {
                atcud ??= docRaw[(slash + 3)..].Trim();
                docNumber = docRaw[..slash].Trim();
            }

            var date = Text.ParseDate(Csv.At(r, cDate));
            var total = Text.ParseAmount(Csv.At(r, cTotal));
            if (nif == null || date == null || total == null || string.IsNullOrWhiteSpace(docNumber))
            {
                if (r.Any(c => c.Length > 0)) errors.Add($"Linha {i + 1} ignorada: dados incompletos");
                continue;
            }

            result.Add(new EFaturaRow(nif, string.IsNullOrWhiteSpace(name) ? null : name, docNumber, atcud == "0" ? null : atcud,
                Csv.At(r, cType), date.Value, total.Value, Text.ParseAmount(Csv.At(r, cVat)), Text.ParseAmount(Csv.At(r, cBase)),
                Csv.At(r, cSector), Csv.At(r, cStatus)));
        }
        return (result, errors);
    }

    [GeneratedRegex(@"^\s*(?:PT)?(?<nif>\d{9})\s*[-–]\s*(?<name>.+)$")] private static partial Regex EmitterRx();
    [GeneratedRegex(@"\d{9}")] private static partial Regex NifRx();
    [GeneratedRegex(@"^[A-Z0-9]{8,}-\d+$")] private static partial Regex AtcudRx();
}

public static class EFaturaEndpoints
{
    public static void MapEFaturaEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orgs/{orgId:guid}/efatura").RequireAuthorization().WithTags("e-Fatura");

        g.MapGet("imports", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            return Results.Ok(await db.EFaturaImports.Where(i => i.OrganizationId == orgId).OrderByDescending(i => i.CreatedAt).ToListAsync(ct));
        });

        g.MapPost("imports", async (Guid orgId, HttpRequest req, AccessService access, CurrentUser me, EFaturaMatcher matcher,
            AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault() ?? throw new AppException(400, "Envie o ficheiro CSV do e-Fatura");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            var (rows, errors) = EFaturaCsvParser.Parse(ms.ToArray());
            if (rows.Count == 0) throw new AppException(400, errors.FirstOrDefault() ?? "Ficheiro sem registos");

            var periodStart = Text.ParseDate(form["periodStart"]) ?? rows.Min(r => r.IssueDate);
            var periodEnd = Text.ParseDate(form["periodEnd"]) ?? rows.Max(r => r.IssueDate);

            var import = new EFaturaImport
            {
                OrganizationId = orgId, ImportedByUserId = me.Id, FileName = file.FileName,
                PeriodStart = periodStart, PeriodEnd = periodEnd, RecordsTotal = rows.Count
            };
            db.EFaturaImports.Add(import);

            var keys = rows.Select(r => EFaturaMatcher.Key(r.SupplierNif, r.DocumentNumber)).ToHashSet();
            var existing = await db.EFaturaRecords.Where(r => r.OrganizationId == orgId && keys.Contains(r.UniqueKey))
                .ToDictionaryAsync(r => r.UniqueKey, ct);

            var touched = new List<EFaturaRecord>();
            foreach (var row in rows)
            {
                var key = EFaturaMatcher.Key(row.SupplierNif, row.DocumentNumber);
                if (existing.TryGetValue(key, out var rec))
                {
                    // Already known from an earlier import: refresh AT status/values, never duplicate.
                    rec.AtStatus = row.Status ?? rec.AtStatus;
                    rec.Total = row.Total;
                    rec.Vat = row.Vat ?? rec.Vat;
                    rec.Base = row.Base ?? rec.Base;
                    rec.Atcud ??= row.Atcud;
                    import.RecordsAlreadyKnown++;
                }
                else
                {
                    rec = new EFaturaRecord
                    {
                        OrganizationId = orgId, ImportId = import.Id, UniqueKey = key,
                        SupplierNif = Text.NormalizeNif(row.SupplierNif), SupplierName = row.SupplierName,
                        DocumentNumber = row.DocumentNumber, Atcud = row.Atcud, DocumentType = row.DocumentType,
                        IssueDate = row.IssueDate, Total = row.Total, Vat = row.Vat, Base = row.Base,
                        Sector = row.Sector, AtStatus = row.Status
                    };
                    db.EFaturaRecords.Add(rec);
                    existing[key] = rec;
                    import.RecordsNew++;
                }
                touched.Add(rec);
            }

            import.RecordsMatched = await matcher.MatchRecordsAsync(orgId, touched, ct);
            audit.Log(orgId, me.Id, "efatura.import", nameof(EFaturaImport), import.Id, $"{rows.Count} registos");
            await db.SaveChangesAsync(ct);

            var missing = touched.Count(r => r.MatchedInvoiceId == null);
            return Results.Ok(new { import, missing, warnings = errors.Take(50) });
        }).DisableAntiforgery();

        g.MapGet("records", async (Guid orgId, int? year, int? month, bool? matched, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var q = db.EFaturaRecords.Where(r => r.OrganizationId == orgId);
            if (year is { } y) q = q.Where(r => r.IssueDate.Year == y);
            if (month is { } m) q = q.Where(r => r.IssueDate.Month == m);
            if (matched is { } mt) q = mt ? q.Where(r => r.MatchedInvoiceId != null) : q.Where(r => r.MatchedInvoiceId == null);
            var items = await q.OrderByDescending(r => r.IssueDate).Take(2000).ToListAsync(ct);
            var ids = items.Select(r => r.Id).ToList();
            var requested = await db.DocumentRequests
                .Where(d => d.EFaturaRecordId != null && ids.Contains(d.EFaturaRecordId.Value) && d.Status == DocumentRequestStatus.Open)
                .Select(d => d.EFaturaRecordId!.Value).ToListAsync(ct);
            return Results.Ok(items.Select(r => new
            {
                r.Id, r.SupplierNif, r.SupplierName, r.DocumentNumber, r.Atcud, r.DocumentType, r.IssueDate,
                r.Total, r.Vat, r.Base, r.Sector, r.AtStatus, r.MatchedInvoiceId, Requested = requested.Contains(r.Id)
            }));
        });

        g.MapPost("records/{id:guid}/request", async (Guid orgId, Guid id, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.RequireSeeAllAsync(orgId, ct);
            var rec = await db.EFaturaRecords.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct)
                      ?? throw new AppException(404, "Registo não encontrado");
            var description = $"Falta o documento {rec.DocumentNumber} de {rec.SupplierName ?? rec.SupplierNif} ({rec.IssueDate:dd/MM/yyyy}, {rec.Total:0.00} €)";
            db.DocumentRequests.Add(new DocumentRequest
            {
                OrganizationId = orgId, RequestedByUserId = me.Id, EFaturaRecordId = rec.Id, Description = description
            });
            db.Messages.Add(new Message
            {
                OrganizationId = orgId, SenderUserId = me.Id, FromAccountant = a.IsAccountant || a.IsAdmin,
                Subject = "Pedido de documento em falta", Body = description + ". Por favor carregue a fatura no A-Fatura."
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
