using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using AFatura.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Banking;

public record ParsedTransaction(DateOnly Date, DateOnly? ValueDate, string Description, decimal Amount, decimal? Balance);

public record BankColumnMap(int? Date, int? ValueDate, int? Description, int? Amount, int? Debit, int? Credit, int? Balance);

/// <summary>
/// Generic CSV statement parser that understands the usual Portuguese bank exports (CGD, Millennium, Santander,
/// Novo Banco, BPI, ActivoBank, Montepio, Revolut…). Columns are found by alias; an explicit map can override.
/// </summary>
public static class BankStatementParser
{
    public static (List<ParsedTransaction> Rows, List<string> Errors) Parse(byte[] bytes, BankColumnMap? map = null)
    {
        var rows = Csv.Parse(Csv.Decode(bytes));
        var errors = new List<string>();

        var headerIdx = rows.FindIndex(r =>
            Csv.Find(r, "data mov", "data movimento", "data lancamento", "data operacao", "data", "date", "completed date") >= 0 &&
            (Csv.Find(r, "montante", "valor", "importancia", "amount", "debito", "credito") >= 0));
        if (headerIdx < 0 && map == null) return (new(), new() { "Não foi possível identificar o cabeçalho do extrato" });
        var h = headerIdx >= 0 ? rows[headerIdx] : Array.Empty<string>();

        int cDate = map?.Date ?? Csv.Find(h, "data mov", "data movimento", "data lancamento", "data operacao", "data", "date", "completed date", "started date");
        int cValue = map?.ValueDate ?? Csv.Find(h, "data valor", "value date");
        if (cValue == cDate) cValue = -1;
        int cDesc = map?.Description ?? Csv.Find(h, "descricao", "descritivo", "description", "movimento", "detalhe", "referencia");
        int cAmount = map?.Amount ?? Csv.Find(h, "montante", "valor", "importancia", "amount");
        int cDebit = map?.Debit ?? Csv.Find(h, "debito", "debit", "saida");
        int cCredit = map?.Credit ?? Csv.Find(h, "credito", "credit", "entrada");
        int cBalance = map?.Balance ?? Csv.Find(h, "saldo contabilistico", "saldo", "balance");
        if (cAmount == cValue) cAmount = Csv.Find(h, "montante", "importancia", "amount");

        var list = new List<ParsedTransaction>();
        for (var i = Math.Max(headerIdx + 1, 0); i < rows.Count; i++)
        {
            var r = rows[i];
            var date = Text.ParseDate(Csv.At(r, cDate));
            if (date == null) continue; // footers, totals, blank lines

            decimal? amount = null;
            if (cDebit >= 0 || cCredit >= 0)
            {
                var debit = Text.ParseAmount(Csv.At(r, cDebit));
                var credit = Text.ParseAmount(Csv.At(r, cCredit));
                if (debit != null || credit != null) amount = (credit ?? 0) - Math.Abs(debit ?? 0);
            }
            amount ??= Text.ParseAmount(Csv.At(r, cAmount));
            if (amount == null) { errors.Add($"Linha {i + 1}: montante inválido"); continue; }

            list.Add(new ParsedTransaction(date.Value, Text.ParseDate(Csv.At(r, cValue)),
                (Csv.At(r, cDesc) ?? "").Trim(), amount.Value, Text.ParseAmount(Csv.At(r, cBalance))));
        }
        return (list, errors);
    }

    /// <summary>
    /// Stable hash per movement. Identical movements on the same day are told apart by their occurrence index,
    /// so re-importing an overlapping statement never creates duplicates.
    /// </summary>
    public static List<(ParsedTransaction Tx, string Hash)> Hash(IEnumerable<ParsedTransaction> txs)
    {
        var seen = new Dictionary<string, int>();
        var result = new List<(ParsedTransaction, string)>();
        foreach (var t in txs)
        {
            var baseKey = $"{t.Date:yyyyMMdd}|{t.Amount:0.00}|{Text.Fold(t.Description)}";
            seen[baseKey] = seen.TryGetValue(baseKey, out var n) ? n + 1 : 0;
            result.Add((t, Text.Sha256($"{baseKey}|{seen[baseKey]}")));
        }
        return result;
    }
}

public record BankAccountInput(string BankName, string Alias, string? Iban, string? Currency);

public static class BankEndpoints
{
    public static void MapBankEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orgs/{orgId:guid}/bank-accounts").RequireAuthorization().WithTags("Banking");

        g.MapGet("", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var accounts = await db.BankAccounts.Where(b => b.OrganizationId == orgId).OrderBy(b => b.Alias).ToListAsync(ct);
            var ids = accounts.Select(a => a.Id).ToList();
            var imports = await db.BankStatementImports.Where(i => ids.Contains(i.BankAccountId))
                .OrderBy(i => i.PeriodStart).ToListAsync(ct);
            var stats = await db.BankTransactions.Where(t => ids.Contains(t.BankAccountId))
                .GroupBy(t => t.BankAccountId)
                .Select(x => new { Id = x.Key, Count = x.Count(), Matched = x.Count(t => t.MatchedInvoiceId != null), Last = x.Max(t => t.Date) })
                .ToListAsync(ct);
            return Results.Ok(accounts.Select(a => new
            {
                a.Id, a.BankName, a.Alias, a.Iban, a.Currency, a.IsActive,
                Imports = imports.Where(i => i.BankAccountId == a.Id),
                // Loaded periods merged into continuous ranges so the UI can show gaps
                Coverage = MergeRanges(imports.Where(i => i.BankAccountId == a.Id).Select(i => (i.PeriodStart, i.PeriodEnd))),
                Stats = stats.FirstOrDefault(s => s.Id == a.Id)
            }));
        });

        g.MapPost("", async (Guid orgId, BankAccountInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var acc = new BankAccount
            {
                OrganizationId = orgId, BankName = input.BankName.Trim(), Alias = string.IsNullOrWhiteSpace(input.Alias) ? input.BankName : input.Alias.Trim(),
                Iban = input.Iban?.Replace(" ", "").ToUpperInvariant(), Currency = input.Currency ?? "EUR"
            };
            db.BankAccounts.Add(acc);
            await db.SaveChangesAsync(ct);
            return Results.Ok(acc);
        });

        g.MapPut("{id:guid}", async (Guid orgId, Guid id, BankAccountInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var acc = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.OrganizationId == orgId, ct) ?? throw new AppException(404, "Conta não encontrada");
            acc.BankName = input.BankName; acc.Alias = input.Alias; acc.Iban = input.Iban; acc.Currency = input.Currency ?? acc.Currency;
            await db.SaveChangesAsync(ct);
            return Results.Ok(acc);
        });

        g.MapPost("{id:guid}/statements", async (Guid orgId, Guid id, HttpRequest req, AccessService access, CurrentUser me,
            FileStorage storage, PdfStatementReader pdfReader, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var acc = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.OrganizationId == orgId, ct) ?? throw new AppException(404, "Conta não encontrada");
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault() ?? throw new AppException(400, "Envie o extrato em CSV ou PDF");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var isPdf = file.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                        || file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            // The original file is kept (and the PDF must be on disk for the AI to read it).
            var stored = await storage.SaveAsync(orgId, file.FileName, isPdf ? "application/pdf" : file.ContentType, bytes, ct);

            List<ParsedTransaction> rows;
            List<string> errors;
            DateOnly? detectedStart = null, detectedEnd = null;
            string method = "CSV";
            if (isPdf)
            {
                var r = await pdfReader.ReadAsync(storage.FullPath(stored), bytes, ct);
                (rows, errors, detectedStart, detectedEnd, method) = (r.Rows, r.Warnings, r.PeriodStart, r.PeriodEnd, r.Method);
            }
            else (rows, errors) = BankStatementParser.Parse(bytes);
            if (rows.Count == 0)
            {
                await db.SaveChangesAsync(ct); // keep the uploaded file for reference
                throw new AppException(400, errors.FirstOrDefault() ?? "Extrato sem movimentos");
            }

            var periodStart = Text.ParseDate(form["periodStart"]) ?? detectedStart ?? rows.Min(r => r.Date);
            var periodEnd = Text.ParseDate(form["periodEnd"]) ?? detectedEnd ?? rows.Max(r => r.Date);

            var overlapping = await db.BankStatementImports
                .Where(i => i.BankAccountId == id && i.PeriodStart <= periodEnd && i.PeriodEnd >= periodStart)
                .OrderBy(i => i.PeriodStart).ToListAsync(ct);

            var import = new BankStatementImport
            {
                BankAccountId = id, ImportedByUserId = me.Id, FileId = stored.Id, FileName = file.FileName,
                PeriodStart = periodStart, PeriodEnd = periodEnd, TransactionsTotal = rows.Count,
                OverlapWarning = overlapping.Count == 0 ? null :
                    "Período sobrepõe-se a importações anteriores: " +
                    string.Join(", ", overlapping.Select(o => $"{o.PeriodStart:dd/MM/yyyy}–{o.PeriodEnd:dd/MM/yyyy}")) +
                    ". Movimentos repetidos foram ignorados."
            };
            db.BankStatementImports.Add(import);

            var hashed = BankStatementParser.Hash(rows);
            var hashes = hashed.Select(h => h.Hash).ToList();
            var existing = (await db.BankTransactions.Where(t => t.BankAccountId == id && hashes.Contains(t.Hash))
                .Select(t => t.Hash).ToListAsync(ct)).ToHashSet();

            foreach (var (tx, hash) in hashed)
            {
                if (existing.Contains(hash)) { import.TransactionsDuplicate++; continue; }
                db.BankTransactions.Add(new BankTransaction
                {
                    BankAccountId = id, ImportId = import.Id, Date = tx.Date, ValueDate = tx.ValueDate,
                    Description = tx.Description, Amount = tx.Amount, Balance = tx.Balance, Hash = hash
                });
                import.TransactionsNew++;
            }
            audit.Log(orgId, me.Id, "bank.import", nameof(BankStatementImport), import.Id, acc.Alias);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { import, method, warnings = errors.Take(50) });
        }).DisableAntiforgery();

        // Remove a statement import. Movements that also appear in another (overlapping) statement of the same account
        // are kept and moved to that statement — in the other import they had been skipped as duplicates.
        g.MapDelete("{id:guid}/statements/{importId:guid}", async (Guid orgId, Guid id, Guid importId, AccessService access,
            CurrentUser me, FileStorage storage, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            if (!await db.BankAccounts.AnyAsync(b => b.Id == id && b.OrganizationId == orgId, ct)) throw new AppException(404, "Conta não encontrada");
            var import = await db.BankStatementImports.FirstOrDefaultAsync(i => i.Id == importId && i.BankAccountId == id, ct)
                         ?? throw new AppException(404, "Importação não encontrada");

            var txs = await db.BankTransactions.Where(t => t.ImportId == importId).ToListAsync(ct);
            var warnings = new List<string>();
            var moved = 0;
            if (txs.Count > 0)
            {
                var others = await db.BankStatementImports
                    .Where(i => i.BankAccountId == id && i.Id != importId && i.PeriodStart <= import.PeriodEnd && i.PeriodEnd >= import.PeriodStart)
                    .ToListAsync(ct);
                var remaining = txs.ToDictionary(t => t.Hash);
                foreach (var other in others)
                {
                    if (remaining.Count == 0) break;
                    var file = other.FileId is { } fid ? await db.Files.FirstOrDefaultAsync(f => f.Id == fid, ct) : null;
                    var isCsv = file != null && !file.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
                    if (!isCsv || !File.Exists(storage.FullPath(file!)))
                    {
                        warnings.Add($"O extrato \"{other.FileName}\" ({other.PeriodStart:dd/MM/yyyy}–{other.PeriodEnd:dd/MM/yyyy}) sobrepõe-se a este e não pôde ser verificado automaticamente; se faltarem movimentos, volte a carregá-lo.");
                        continue;
                    }
                    var (rows, _) = BankStatementParser.Parse(await storage.ReadAsync(file!, ct));
                    var hashes = BankStatementParser.Hash(rows).Select(h => h.Hash).ToHashSet();
                    foreach (var hash in remaining.Keys.Where(hashes.Contains).ToList())
                    {
                        remaining[hash].ImportId = other.Id;
                        remaining.Remove(hash);
                        other.TransactionsNew++;
                        other.TransactionsDuplicate = Math.Max(0, other.TransactionsDuplicate - 1);
                        moved++;
                    }
                }
                db.BankTransactions.RemoveRange(remaining.Values);
            }
            db.BankStatementImports.Remove(import);
            audit.Log(orgId, me.Id, "bank.import.delete", nameof(BankStatementImport), import.Id, $"{import.FileName}: -{txs.Count - moved}, mantidos {moved}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { deleted = txs.Count - moved, kept = moved, warnings });
        });

        // Remove every import of the account that brought nothing new (all movements were duplicates).
        g.MapDelete("{id:guid}/statements/duplicates", async (Guid orgId, Guid id, AccessService access, CurrentUser me,
            AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            if (!await db.BankAccounts.AnyAsync(b => b.Id == id && b.OrganizationId == orgId, ct)) throw new AppException(404, "Conta não encontrada");
            var empty = await db.BankStatementImports
                .Where(i => i.BankAccountId == id && !db.BankTransactions.Any(t => t.ImportId == i.Id))
                .ToListAsync(ct);
            db.BankStatementImports.RemoveRange(empty);
            audit.Log(orgId, me.Id, "bank.import.delete-duplicates", data: $"{empty.Count} importações");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = empty.Count });
        });

        g.MapGet("{id:guid}/transactions", async (Guid orgId, Guid id, int? year, int? month, bool? unmatched, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            if (!await db.BankAccounts.AnyAsync(b => b.Id == id && b.OrganizationId == orgId, ct)) throw new AppException(404, "Conta não encontrada");
            var q = db.BankTransactions.Where(t => t.BankAccountId == id);
            if (year is { } y) q = q.Where(t => t.Date.Year == y);
            if (month is { } m) q = q.Where(t => t.Date.Month == m);
            if (unmatched == true) q = q.Where(t => t.MatchedInvoiceId == null);
            return Results.Ok(await q.OrderByDescending(t => t.Date).Take(3000).ToListAsync(ct));
        });

        // Reconciliation: suggest invoices with the same amount within a date window for each unmatched debit.
        g.MapGet("{id:guid}/suggestions", async (Guid orgId, Guid id, int year, int month, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var from = new DateOnly(year, month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var txs = await db.BankTransactions.Where(t => t.BankAccountId == id && t.MatchedInvoiceId == null && t.Amount < 0
                                                         && t.Date >= from && t.Date <= to).ToListAsync(ct);
            var invoices = await db.Invoices.Where(i => i.OrganizationId == orgId && i.DuplicateOfId == null && i.TotalAmount != null
                                                         && i.IssueDate >= from.AddDays(-15) && i.IssueDate <= to.AddDays(5))
                .Select(i => new { i.Id, i.SupplierName, i.DocumentNumber, i.IssueDate, i.TotalAmount }).ToListAsync(ct);
            var orgAccountIds = db.BankAccounts.Where(b => b.OrganizationId == orgId).Select(b => b.Id);
            var matchedIds = (await db.BankTransactions.Where(t => t.MatchedInvoiceId != null && orgAccountIds.Contains(t.BankAccountId)).Select(t => t.MatchedInvoiceId!.Value).ToListAsync(ct)).ToHashSet();

            var suggestions = txs.Select(t => new
            {
                Transaction = t,
                Candidates = invoices.Where(i => !matchedIds.Contains(i.Id) && Math.Abs(i.TotalAmount!.Value + t.Amount) <= 0.01m
                                                 && i.IssueDate!.Value.DayNumber - t.Date.DayNumber is >= -15 and <= 5)
                    .OrderBy(i => Math.Abs(i.IssueDate!.Value.DayNumber - t.Date.DayNumber)).Take(3).ToList()
            }).Where(x => x.Candidates.Count > 0);
            return Results.Ok(suggestions);
        });

        g.MapPost("{id:guid}/transactions/{txId:guid}/match", async (Guid orgId, Guid id, Guid txId, MatchInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var tx = await db.BankTransactions.FirstOrDefaultAsync(t => t.Id == txId && t.BankAccountId == id, ct) ?? throw new AppException(404, "Movimento não encontrado");
            if (input.InvoiceId is { } invId && !await db.Invoices.AnyAsync(i => i.Id == invId && i.OrganizationId == orgId, ct))
                throw new AppException(400, "Fatura inválida");
            tx.MatchedInvoiceId = input.InvoiceId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public record MatchInput(Guid? InvoiceId);

    private static List<object> MergeRanges(IEnumerable<(DateOnly Start, DateOnly End)> ranges)
    {
        var merged = new List<(DateOnly Start, DateOnly End)>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && r.Start.DayNumber <= merged[^1].End.DayNumber + 1)
                merged[^1] = (merged[^1].Start, r.End > merged[^1].End ? r.End : merged[^1].End);
            else merged.Add(r);
        }
        return merged.Select(m => (object)new { start = m.Start, end = m.End }).ToList();
    }
}
