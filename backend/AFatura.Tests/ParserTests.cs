using System.Text;
using AFatura.Api.Common;
using AFatura.Api.Features.Banking;
using AFatura.Api.Features.EFatura;
using AFatura.Api.Features.Extraction;
using Xunit;

namespace AFatura.Tests;

public class ParserTests
{
    static ParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Parses_AT_QR_code()
    {
        const string raw = "A:123456789*B:999999990*C:PT*D:FS*E:N*F:20250426*G:FS FCA-0125/039782*H:JJ9N48DB-039782*I1:PT*I3:10.00*I4:0.60*I7:100.00*I8:23.00*N:23.60*O:133.60*Q:abcd*R:1234";
        var q = AtQrCodeParser.TryParse(raw)!;
        Assert.Equal("123456789", q.SupplierNif);
        Assert.Null(q.BuyerNif); // consumidor final
        Assert.Equal(new DateOnly(2025, 4, 26), q.IssueDate);
        Assert.Equal("JJ9N48DB-039782", q.Atcud);
        Assert.Equal(2, q.VatLines.Count);
        Assert.Equal(23m, q.VatLines[1].Rate);
        Assert.Equal(133.60m, q.Total);
        Assert.Equal(23.60m, q.TotalTaxes);
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-12,30 €", -12.30)]
    [InlineData("12,30-", -12.30)]
    [InlineData("99.9", 99.9)]
    public void Parses_amounts(string input, double expected) => Assert.Equal((decimal)expected, Text.ParseAmount(input));

    [Theory]
    [InlineData("501964843", true)]
    [InlineData("123456789", true)]
    [InlineData("123456780", false)]
    public void Validates_nif(string nif, bool valid) => Assert.Equal(valid, Text.IsValidPtNif(nif));

    [Fact]
    public void Parses_efatura_csv()
    {
        var csv = "Setor;Emitente;Nº Fatura / ATCUD;Tipo;Data Emissão;Total;IVA;Base Tributável;Situação\n" +
                  "Outros;503504564 - GALP ENERGIA SA;FS 2025A/1234 / ABCD1234-1234;Fatura simplificada;12-03-2025;61,50 €;11,50 €;50,00 €;Registado\n";
        var (rows, errors) = EFaturaCsvParser.Parse(Encoding.GetEncoding(1252).GetBytes(csv));
        Assert.Empty(errors);
        var r = Assert.Single(rows);
        Assert.Equal("503504564", r.SupplierNif);
        Assert.Equal("GALP ENERGIA SA", r.SupplierName);
        Assert.Equal("FS 2025A/1234", r.DocumentNumber);
        Assert.Equal("ABCD1234-1234", r.Atcud);
        Assert.Equal(61.50m, r.Total);
    }

    [Fact]
    public void Bank_hash_is_stable_and_handles_identical_movements()
    {
        var csv = "Data Mov.;Data Valor;Descrição;Débito;Crédito;Saldo\n" +
                  "01-03-2025;01-03-2025;COMPRA GALP;50,00;;1000,00\n" +
                  "01-03-2025;01-03-2025;COMPRA GALP;50,00;;950,00\n" +
                  "02-03-2025;02-03-2025;TRF RECEBIDA;;200,00;1150,00\n";
        var (rows, _) = BankStatementParser.Parse(Encoding.UTF8.GetBytes(csv));
        Assert.Equal(3, rows.Count);
        Assert.Equal(-50m, rows[0].Amount);
        Assert.Equal(200m, rows[2].Amount);
        var h1 = BankStatementParser.Hash(rows).Select(x => x.Hash).ToList();
        var h2 = BankStatementParser.Hash(rows).Select(x => x.Hash).ToList();
        Assert.Equal(h1, h2);
        Assert.Equal(3, h1.Distinct().Count());
    }
}

public class PdfStatementTests
{
    [Fact]
    public void Parses_statement_text_lines_using_balance_delta_for_sign()
    {
        var lines = new List<string>
        {
            "EXTRATO DE CONTA  Período 01-03-2025 a 31-03-2025",
            "Data Mov. Data Valor Descritivo Montante Saldo",
            "SALDO ANTERIOR 1.000,00",
            "03.03 03.03 COMPRA GALP LISBOA 50,00 950,00",
            "05.03 05.03 TRF RECEBIDA CLIENTE X 1.200,00 2.150,00",
            "10.03 10.03 PAG SERV EDP 82,35 2.067,65",
            "SALDO FINAL 2.067,65",
        };
        var (rows, opening, _) = AFatura.Api.Features.Banking.PdfStatementReader.ParseText(lines);
        Assert.Equal(1000m, opening);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new DateOnly(2025, 3, 3), rows[0].Date);
        Assert.Equal(-50m, rows[0].Amount);
        Assert.Equal(1200m, rows[1].Amount);
        Assert.Equal(-82.35m, rows[2].Amount);
        Assert.Equal("PAG SERV EDP", rows[2].Description);
        Assert.Empty(AFatura.Api.Features.Banking.PdfStatementReader.CheckBalances(rows, opening, 2067.65m));
    }
}

public class EFaturaMatcherTests
{
    [Fact]
    public void Multi_invoice_document_matches_each_line()
    {
        var inv = new AFatura.Api.Domain.Invoice
        {
            SupplierNif = "504709275",
            SubDocuments =
            [
                new() { SupplierNif = "504709275", DocumentNumber = "FT VV/1001", Total = 3.10m },
                new() { SupplierNif = "504709275", DocumentNumber = "FT VV/1002", Total = 7.45m },
            ]
        };
        var records = new List<AFatura.Api.Domain.EFaturaRecord>
        {
            new() { SupplierNif = "504709275", DocumentNumber = "FT VV/1002", Total = 7.45m },
            new() { SupplierNif = "504709275", DocumentNumber = "FT VV/1001", Total = 3.10m },
            new() { SupplierNif = "504709275", DocumentNumber = "FT VV/1003", Total = 1.20m },
        };
        var parts = AFatura.Api.Features.EFatura.EFaturaMatcher.Parts(inv);
        Assert.Equal(2, parts.Count);
        var matched = parts.Select(p => AFatura.Api.Features.EFatura.EFaturaMatcher.Best(p, records)?.DocumentNumber).ToList();
        Assert.Equal(new[] { "FT VV/1001", "FT VV/1002" }, matched);
    }
}
