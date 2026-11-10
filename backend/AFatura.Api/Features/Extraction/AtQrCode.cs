using System.Globalization;
using AFatura.Api.Domain;

namespace AFatura.Api.Features.Extraction;

/// <summary>
/// Parsed content of the fiscal QR code mandatory on Portuguese invoices (Portaria 195/2020, AT technical spec v1.1).
/// Example: A:123456789*B:999999990*C:PT*D:FT*E:N*F:20191231*G:FT AB2019/0035*H:CSDF7T5H-0035*I1:PT*I7:100.00*I8:23.00*N:23.00*O:123.00*Q:sBuR*R:9999
/// </summary>
public record AtQrCode(
    string SupplierNif,
    string? BuyerNif,
    string? BuyerCountry,
    string? DocumentType,
    string? DocumentStatus,
    DateOnly? IssueDate,
    string? DocumentNumber,
    string? Atcud,
    List<VatLine> VatLines,
    decimal? NotSubjectAmount,
    decimal? StampDuty,
    decimal? TotalTaxes,
    decimal? Total,
    decimal? WithholdingTax,
    string? CertificateNumber,
    string Raw)
{
    public decimal NetAmount => VatLines.Sum(v => v.Base) + (NotSubjectAmount ?? 0);
}

public static class AtQrCodeParser
{
    // Regional rates: reduced / intermediate / normal (for display; QR carries only base and VAT per band)
    private static readonly Dictionary<string, decimal[]> Rates = new()
    {
        ["PT"] = [6m, 13m, 23m],
        ["PT-AC"] = [4m, 9m, 16m],
        ["PT-MA"] = [5m, 12m, 22m], // confirm current Madeira rates when tax law changes
    };

    public static bool LooksLikeAtQr(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith("A:") && raw.Contains("*B:");

    public static AtQrCode? TryParse(string? raw)
    {
        if (!LooksLikeAtQr(raw)) return null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw!.Trim().Split('*'))
        {
            var idx = part.IndexOf(':');
            if (idx <= 0) continue;
            fields[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }
        if (!fields.TryGetValue("A", out var supplierNif) || string.IsNullOrWhiteSpace(supplierNif)) return null;

        var vat = new List<VatLine>();
        AddRegion(fields, "I", vat);
        AddRegion(fields, "J", vat);
        AddRegion(fields, "K", vat);

        DateOnly? date = null;
        if (fields.TryGetValue("F", out var f) && DateOnly.TryParseExact(f, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            date = d;

        var atcud = Get(fields, "H");
        if (atcud == "0") atcud = null;

        return new AtQrCode(
            supplierNif,
            Get(fields, "B") is { } b && b != "999999990" ? b : null, // 999999990 = consumidor final
            Get(fields, "C"),
            Get(fields, "D"),
            Get(fields, "E"),
            date,
            Get(fields, "G"),
            atcud,
            vat,
            Dec(fields, "L"),
            Dec(fields, "M"),
            Dec(fields, "N"),
            Dec(fields, "O"),
            Dec(fields, "P"),
            Get(fields, "R"),
            raw);
    }

    /// <summary>
    /// Region block: X1 = fiscal space; X2 = exempt base; X3/X4 = reduced base/VAT; X5/X6 = intermediate; X7/X8 = normal.
    /// </summary>
    private static void AddRegion(Dictionary<string, string> fields, string prefix, List<VatLine> vat)
    {
        if (!fields.TryGetValue(prefix + "1", out var region) || region == "0") return;
        var rates = Rates.TryGetValue(region, out var r) ? r : Rates["PT"];

        if (Dec(fields, prefix + "2") is { } exempt && exempt != 0)
            vat.Add(new VatLine { Region = region, Rate = 0, Base = exempt, Vat = 0 });
        AddBand(fields, prefix + "3", prefix + "4", region, rates[0], vat);
        AddBand(fields, prefix + "5", prefix + "6", region, rates[1], vat);
        AddBand(fields, prefix + "7", prefix + "8", region, rates[2], vat);
    }

    private static void AddBand(Dictionary<string, string> f, string baseKey, string vatKey, string region, decimal rate, List<VatLine> vat)
    {
        var b = Dec(f, baseKey);
        var v = Dec(f, vatKey);
        if ((b ?? 0) == 0 && (v ?? 0) == 0) return;
        vat.Add(new VatLine { Region = region, Rate = rate, Base = b ?? 0, Vat = v ?? 0 });
    }

    private static string? Get(Dictionary<string, string> f, string k) =>
        f.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static decimal? Dec(Dictionary<string, string> f, string k) =>
        f.TryGetValue(k, out var v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
