using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AFatura.Api.Data;
using AFatura.Api.Domain;

namespace AFatura.Api.Common;

public class AppException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static partial class Text
{
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static string Sha256(string s) => Sha256(Encoding.UTF8.GetBytes(s));

    public static string RandomToken(int bytes = 12) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace("+", "").Replace("/", "").Replace("=", "").ToLowerInvariant();

    public static string RandomPassword() => $"Cf-{RandomToken(9)}!";

    /// <summary>Lowercase, no accents, collapsed spaces — for keyword matching.</summary>
    public static string Fold(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Document numbers are compared ignoring spaces/case: "FT 2024A/12" == "ft2024a/12".</summary>
    public static string NormalizeDocNumber(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : NonDocChars().Replace(s.ToUpperInvariant(), "");

    public static string NormalizePlate(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : NonAlnum().Replace(s.ToUpperInvariant(), "");

    public static string NormalizeNif(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = NonAlnum().Replace(s.ToUpperInvariant(), "");
        return t.StartsWith("PT") && t.Length == 11 ? t[2..] : t;
    }

    /// <summary>Portuguese NIF check digit (mod 11).</summary>
    public static bool IsValidPtNif(string? nif)
    {
        nif = NormalizeNif(nif);
        if (nif.Length != 9 || !nif.All(char.IsDigit)) return false;
        var sum = 0;
        for (var i = 0; i < 8; i++) sum += (nif[i] - '0') * (9 - i);
        var check = 11 - sum % 11;
        if (check >= 10) check = 0;
        return check == nif[8] - '0';
    }

    /// <summary>Parses "1.234,56", "1234.56", "-12,30 €", "12,30-" etc.</summary>
    public static decimal? ParseAmount(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().Replace("€", "").Replace("EUR", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "");
        var negative = false;
        if (t.EndsWith('-')) { negative = true; t = t[..^1]; }
        if (t.StartsWith('(') && t.EndsWith(')')) { negative = true; t = t[1..^1]; }
        if (t.StartsWith('-')) { negative = true; t = t[1..]; }
        else if (t.StartsWith('+')) t = t[1..];
        if (t.Length == 0) return null;

        var lastComma = t.LastIndexOf(',');
        var lastDot = t.LastIndexOf('.');
        if (lastComma > lastDot) t = t.Replace(".", "").Replace(',', '.');          // 1.234,56
        else if (lastDot > lastComma && lastComma >= 0) t = t.Replace(",", "");      // 1,234.56
        else if (lastComma < 0 && lastDot >= 0 && t.Count(c => c == '.') > 1) t = t.Replace(".", ""); // 1.234.567

        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (negative ? -v : v) : null;
    }

    private static readonly string[] DateFormats =
    [
        "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "yyyyMMdd",
        "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss",
        "d-M-yyyy", "d/M/yyyy", "dd-MM-yy", "dd/MM/yy"
    ];

    public static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return DateOnly.FromDateTime(d);
        if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return DateOnly.FromDateTime(d);
        return null;
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[^A-Z0-9/\-]")] private static partial Regex NonDocChars();
    [GeneratedRegex(@"[^A-Z0-9]")] private static partial Regex NonAlnum();
}

/// <summary>Minimal RFC4180-ish CSV reader with delimiter auto-detection.</summary>
public static class Csv
{
    public static char DetectDelimiter(string headerLine)
    {
        var candidates = new[] { ';', ',', '\t', '|' };
        return candidates.OrderByDescending(c => headerLine.Count(ch => ch == c)).First();
    }

    public static List<string[]> Parse(string content, char? delimiter = null)
    {
        content = content.TrimStart('﻿');
        var firstLine = content.Split('\n', 2)[0];
        var d = delimiter ?? DetectDelimiter(firstLine);
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"') inQuotes = true;
            else if (c == d) { row.Add(field.ToString().Trim()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                row.Add(field.ToString().Trim()); field.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(row.ToArray());
                row = [];
            }
            else field.Append(c);
        }
        row.Add(field.ToString().Trim());
        if (row.Any(x => x.Length > 0)) rows.Add(row.ToArray());
        return rows;
    }

    /// <summary>Decodes bytes as UTF-8, falling back to Windows-1252 (common in AT/bank exports).</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>Finds the index of the first header that matches one of the aliases (accent/case insensitive).</summary>
    public static int Find(string[] headers, params string[] aliases)
    {
        var folded = headers.Select(Text.Fold).ToArray();
        foreach (var alias in aliases.Select(Text.Fold))
        {
            var exact = Array.IndexOf(folded, alias);
            if (exact >= 0) return exact;
        }
        foreach (var alias in aliases.Select(Text.Fold))
            for (var i = 0; i < folded.Length; i++)
                if (folded[i].Contains(alias)) return i;
        return -1;
    }

    public static string? At(string[] row, int idx) => idx >= 0 && idx < row.Length ? row[idx] : null;

    public static string Escape(string? s)
    {
        s ??= "";
        return s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}

public class AuditService(AppDbContext db)
{
    public void Log(Guid? orgId, Guid? userId, string action, string? entityType = null, Guid? entityId = null, string? data = null) =>
        db.AuditLogs.Add(new AuditLog { OrganizationId = orgId, UserId = userId, Action = action, EntityType = entityType, EntityId = entityId, Data = data });
}
