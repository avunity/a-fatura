using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AFatura.Api.Common;
using AFatura.Api.Features.Extraction;
using UglyToad.PdfPig;

namespace AFatura.Api.Features.Banking;

public record PdfStatementResult(List<ParsedTransaction> Rows, DateOnly? PeriodStart, DateOnly? PeriodEnd, List<string> Warnings, string Method);

/// <summary>
/// Reads bank statements in PDF. 1) AI reads the document and returns every movement as JSON (works with any bank layout);
/// 2) fallback without AI: text extraction (PdfPig) + line heuristics. Both are checked against the balances.
/// </summary>
public partial class PdfStatementReader(IAiExtractor ai, ILogger<PdfStatementReader> log)
{
    private const string Prompt = """
        É um extrato bancário português. Extrai TODOS os movimentos de todas as páginas e responde APENAS com um objeto JSON válido
        (sem markdown, sem texto extra) com esta forma:
        {
          "bank": "nome do banco",
          "iban": "IBAN ou null",
          "periodStart": "yyyy-MM-dd",
          "periodEnd": "yyyy-MM-dd",
          "openingBalance": 0.00,
          "closingBalance": 0.00,
          "transactions": [
            {"date": "yyyy-MM-dd", "valueDate": "yyyy-MM-dd ou null", "description": "descritivo do movimento", "amount": -12.34, "balance": 100.00}
          ]
        }
        Regras: amount NEGATIVO para débitos/saídas/pagamentos e POSITIVO para créditos/entradas; balance = saldo após o movimento ou null;
        não incluas as linhas de saldo anterior/inicial/final como movimentos; mantém a ordem do extrato; números com ponto decimal;
        não inventes movimentos nem valores.
        """;

    public async Task<PdfStatementResult> ReadAsync(string filePath, byte[] data, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (ai.Enabled)
        {
            try
            {
                var text = await ai.CompleteAsync(Prompt, filePath, "application/pdf", data, 16000, ct);
                var parsed = ParseAiJson(text);
                if (parsed is { Transactions.Count: > 0 })
                {
                    var rows = parsed.Transactions
                        .Select(t => Text.ParseDate(t.Date) is { } d
                            ? new ParsedTransaction(d, Text.ParseDate(t.ValueDate), (t.Description ?? "").Trim(), t.Amount, t.Balance)
                            : null)
                        .OfType<ParsedTransaction>().ToList();
                    warnings.AddRange(CheckBalances(rows, parsed.OpeningBalance, parsed.ClosingBalance));
                    return new PdfStatementResult(rows, Text.ParseDate(parsed.PeriodStart), Text.ParseDate(parsed.PeriodEnd), warnings, "IA");
                }
                warnings.Add("A IA não devolveu movimentos; a usar leitura de texto do PDF.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "AI bank statement extraction failed");
                warnings.Add("IA indisponível; a usar leitura de texto do PDF.");
            }
        }

        var lines = ExtractLines(data);
        var (txs, opening, textWarnings) = ParseText(lines);
        warnings.AddRange(textWarnings);
        warnings.AddRange(CheckBalances(txs, opening, null));
        if (txs.Count == 0) warnings.Add("Não foi possível identificar movimentos no PDF (pode ser um PDF digitalizado — ative a IA).");
        return new PdfStatementResult(txs, null, null, warnings, "Texto");
    }

    // ── AI JSON ──
    private sealed class AiStatement
    {
        public string? PeriodStart { get; set; }
        public string? PeriodEnd { get; set; }
        public decimal? OpeningBalance { get; set; }
        public decimal? ClosingBalance { get; set; }
        public List<AiTx> Transactions { get; set; } = [];
    }
    private sealed class AiTx
    {
        public string? Date { get; set; }
        public string? ValueDate { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public decimal? Balance { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true
    };

    private static AiStatement? ParseAiJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonSerializer.Deserialize<AiStatement>(text[start..(end + 1)], Json); }
        catch (JsonException) { return null; }
    }

    // ── Validation ──
    public static List<string> CheckBalances(IReadOnlyList<ParsedTransaction> rows, decimal? opening, decimal? closing)
    {
        var warnings = new List<string>();
        if (rows.Count == 0) return warnings;
        var sum = rows.Sum(r => r.Amount);
        if (opening is { } o && closing is { } c && Math.Abs(o + sum - c) > 0.05m)
            warnings.Add($"Os movimentos não batem com os saldos: inicial {o:0.00} + movimentos {sum:0.00} ≠ final {c:0.00}. Confirme o extrato.");

        var breaks = 0;
        decimal? prev = opening;
        foreach (var r in rows)
        {
            if (prev is { } p && r.Balance is { } b && Math.Abs(p + r.Amount - b) > 0.05m) breaks++;
            prev = r.Balance ?? (prev + r.Amount);
        }
        if (breaks > 0) warnings.Add($"{breaks} movimento(s) não batem com o saldo indicado na linha — verifique sinais ou valores.");
        return warnings;
    }

    // ── Text fallback ──
    /// <summary>Rebuilds visual lines from word positions (same baseline → same line, ordered left to right).</summary>
    public static List<string> ExtractLines(byte[] data)
    {
        var lines = new List<string>();
        using var doc = PdfDocument.Open(data);
        foreach (var page in doc.GetPages())
        {
            lines.AddRange(page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0))
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))));
        }
        return lines;
    }

    private const string DatePattern = @"\d{1,2}[-/.]\d{1,2}(?:[-/.]\d{2,4})?|\d{4}-\d{2}-\d{2}";
    private const string AmountPattern = @"[-+]?\d{1,3}(?:[.\s]\d{3})*,\d{2}-?|[-+]?\d+\.\d{2}";

    [GeneratedRegex(@"^\s*(?<d1>" + DatePattern + @")\s+(?:(?<d2>" + DatePattern + @")\s+)?(?<desc>.*?)\s+(?<amounts>(?:(?:" + AmountPattern + @")\s*){1,3})$")]
    private static partial Regex LineRx();
    [GeneratedRegex(AmountPattern)] private static partial Regex AmountRx();
    [GeneratedRegex(@"\b(20\d{2})\b")] private static partial Regex YearRx();
    [GeneratedRegex(@"saldo\s+(anterior|inicial)", RegexOptions.IgnoreCase)] private static partial Regex OpeningRx();

    public static (List<ParsedTransaction> Rows, decimal? Opening, List<string> Warnings) ParseText(IReadOnlyList<string> lines)
    {
        var warnings = new List<string>();
        var year = lines.Select(l => YearRx().Match(l)).Where(m => m.Success).Select(m => int.Parse(m.Value))
            .GroupBy(y => y).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault() ?? DateTime.UtcNow.Year;

        decimal? opening = null;
        foreach (var l in lines.Where(l => OpeningRx().IsMatch(l)))
        {
            var amounts = AmountRx().Matches(l);
            if (amounts.Count > 0) { opening = Text.ParseAmount(amounts[^1].Value); break; }
        }

        var rows = new List<ParsedTransaction>();
        decimal? prevBalance = opening;
        var unsigned = 0;
        foreach (var line in lines)
        {
            if (OpeningRx().IsMatch(line)) continue;
            var m = LineRx().Match(line);
            if (!m.Success) continue;
            var date = ParseDate(m.Groups["d1"].Value, year);
            if (date == null) continue;
            var amounts = AmountRx().Matches(m.Groups["amounts"].Value).Select(a => Text.ParseAmount(a.Value)).OfType<decimal>().ToList();
            if (amounts.Count == 0) continue;
            var desc = m.Groups["desc"].Value.Trim();
            if (desc.Length == 0 || Text.Fold(desc).StartsWith("saldo")) continue;

            decimal amount;
            decimal? balance = null;
            if (amounts.Count >= 2)
            {
                balance = amounts[^1];
                // The balance delta gives the sign reliably when the previous balance is known.
                amount = prevBalance is { } p ? balance.Value - p : amounts[0];
            }
            else
            {
                amount = amounts[0];
                if (amount > 0 && !m.Groups["amounts"].Value.Contains('+')) unsigned++;
            }
            rows.Add(new ParsedTransaction(date.Value, ParseDate(m.Groups["d2"].Value, year), desc, amount, balance));
            if (balance != null) prevBalance = balance;
        }
        if (unsigned > 0) warnings.Add($"{unsigned} movimento(s) sem saldo na linha: o sinal (débito/crédito) pode estar errado.");
        return (rows, opening, warnings);
    }

    private static DateOnly? ParseDate(string s, int year)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var full = Text.ParseDate(s);
        if (full != null && s.Length >= 8) return full;
        var parts = s.Split('-', '/', '.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out var d) && int.TryParse(parts[1], out var mo) && mo is >= 1 and <= 12 && d is >= 1 and <= 31)
        {
            try { return new DateOnly(year, mo, Math.Min(d, DateTime.DaysInMonth(year, mo))); } catch { return null; }
        }
        return null;
    }
}
