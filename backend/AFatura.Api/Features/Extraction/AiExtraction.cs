using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFatura.Api.Features.Extraction;

public class AiOptions
{
    /// <summary>ClaudeCli (default, uses the Claude CLI logged in on the VPS), Anthropic (API key) or None.</summary>
    public string Provider { get; set; } = "ClaudeCli";
    public string ClaudeCliPath { get; set; } = "claude";
    public string? Model { get; set; }
    public string? AnthropicApiKey { get; set; }
    public string AnthropicModel { get; set; } = "claude-sonnet-5-5";
    public int TimeoutSeconds { get; set; } = 180;
}

public class AiVatLine
{
    public decimal Rate { get; set; }
    public decimal Base { get; set; }
    public decimal Vat { get; set; }
}

public class AiExtraction
{
    public string? SupplierName { get; set; }
    public string? SupplierNif { get; set; }
    public string? BuyerNif { get; set; }
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Atcud { get; set; }
    public string? IssueDate { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public List<AiVatLine>? VatLines { get; set; }
    public string? LicensePlate { get; set; }
    public decimal? FuelLiters { get; set; }
    public decimal? EnergyKwh { get; set; }
    public string? Category { get; set; }
    public decimal? CategoryConfidence { get; set; }
    public string? Summary { get; set; }
    public bool? IsInvoice { get; set; }
    public decimal? Confidence { get; set; }
}

public interface IAiExtractor
{
    bool Enabled { get; }
    Task<(AiExtraction? Result, string? Raw)> ExtractAsync(string filePath, string contentType, byte[] data,
        IReadOnlyList<string> categories, CancellationToken ct);

    /// <summary>Free-form instruction about one document (e.g. bank statement → JSON). Returns the model text, or null.</summary>
    Task<string?> CompleteAsync(string instructions, string filePath, string contentType, byte[] data, int maxTokens, CancellationToken ct);
}

internal static class AiPrompt
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true
    };

    public static string Build(IReadOnlyList<string> categories, string? filePath = null) => $$"""
        És um assistente de contabilidade portuguesa. {{(filePath != null ? $"Lê o documento em \"{filePath}\"." : "Analisa o documento anexo.")}}
        Extrai os dados da fatura/recibo e responde APENAS com um objeto JSON válido (sem markdown, sem texto extra) com esta forma:
        {
          "isInvoice": true,
          "supplierName": "nome do emitente",
          "supplierNif": "NIF do emitente (9 dígitos, sem PT)",
          "buyerNif": "NIF do adquirente ou null",
          "documentType": "FT|FS|FR|NC|ND|RC|outro",
          "documentNumber": "número completo, ex: FT 2025A/123",
          "atcud": "ATCUD ou null",
          "issueDate": "yyyy-MM-dd",
          "netAmount": 0.00,
          "vatAmount": 0.00,
          "totalAmount": 0.00,
          "currency": "EUR",
          "vatLines": [{"rate": 23, "base": 0.00, "vat": 0.00}],
          "licensePlate": "matrícula se aparecer (ex: AA-00-BB) ou null",
          "fuelLiters": null,
          "energyKwh": null,
          "category": "uma das categorias abaixo",
          "categoryConfidence": 0.0,
          "summary": "descrição curta do que foi comprado (máx. 120 caracteres)",
          "confidence": 0.0
        }
        Categorias disponíveis: {{string.Join(" | ", categories)}}
        Regras: números com ponto decimal; null quando não souberes; não inventes NIFs; confidence entre 0 e 1.
        """;

    public static AiExtraction? ParseObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonSerializer.Deserialize<AiExtraction>(text[start..(end + 1)], Json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Uses the Claude CLI already authenticated on the VPS (mounted ~/.claude into the container).
/// Runs: claude -p "&lt;prompt&gt;" --output-format json --allowedTools Read --add-dir &lt;dir&gt;
/// </summary>
public class ClaudeCliExtractor(Microsoft.Extensions.Options.IOptions<AiOptions> options, ILogger<ClaudeCliExtractor> log) : IAiExtractor
{
    private readonly AiOptions _o = options.Value;
    private static readonly SemaphoreSlim Gate = new(2); // keep CLI concurrency low

    public bool Enabled => true;

    public async Task<(AiExtraction? Result, string? Raw)> ExtractAsync(string filePath, string contentType, byte[] data,
        IReadOnlyList<string> categories, CancellationToken ct)
    {
        var text = await RunAsync(AiPrompt.Build(categories, filePath), filePath, ct);
        return (AiPrompt.ParseObject(text), text);
    }

    public Task<string?> CompleteAsync(string instructions, string filePath, string contentType, byte[] data, int maxTokens, CancellationToken ct) =>
        RunAsync($"Lê o documento em \"{filePath}\".\n{instructions}", filePath, ct);

    private async Task<string?> RunAsync(string prompt, string filePath, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(filePath)!;
            var psi = new ProcessStartInfo(_o.ClaudeCliPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = dir,
            };
            foreach (var arg in new[] { "-p", prompt, "--output-format", "json", "--allowedTools", "Read", "--add-dir", dir })
                psi.ArgumentList.Add(arg);
            if (!string.IsNullOrWhiteSpace(_o.Model))
            {
                psi.ArgumentList.Add("--model");
                psi.ArgumentList.Add(_o.Model);
            }

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start claude CLI");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_o.TimeoutSeconds));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(timeout.Token);
            try { await proc.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { proc.Kill(true); } catch { /* ignore */ } throw; }

            var stdout = await stdoutTask;
            if (proc.ExitCode != 0)
            {
                log.LogWarning("claude CLI exited {Code}: {Err}", proc.ExitCode, await stderrTask);
                return null;
            }

            // --output-format json → {"type":"result","result":"<model text>",...}
            try
            {
                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("result", out var r)) return r.GetString();
            }
            catch (JsonException) { /* plain text output */ }
            return stdout;
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>Fallback using the Anthropic Messages API directly (needs an API key).</summary>
public class AnthropicApiExtractor(HttpClient http, Microsoft.Extensions.Options.IOptions<AiOptions> options, ILogger<AnthropicApiExtractor> log) : IAiExtractor
{
    private readonly AiOptions _o = options.Value;
    public bool Enabled => !string.IsNullOrWhiteSpace(_o.AnthropicApiKey);

    public async Task<(AiExtraction? Result, string? Raw)> ExtractAsync(string filePath, string contentType, byte[] data,
        IReadOnlyList<string> categories, CancellationToken ct)
    {
        var text = await SendAsync(AiPrompt.Build(categories), contentType, data, 1500, ct);
        return (AiPrompt.ParseObject(text), text);
    }

    public Task<string?> CompleteAsync(string instructions, string filePath, string contentType, byte[] data, int maxTokens, CancellationToken ct) =>
        SendAsync("Analisa o documento anexo.\n" + instructions, contentType, data, maxTokens, ct);

    private async Task<string?> SendAsync(string prompt, string contentType, byte[] data, int maxTokens, CancellationToken ct)
    {
        var isPdf = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
        var mediaType = isPdf ? "application/pdf" : contentType switch
        {
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            "image/gif" => "image/gif",
            _ => "image/jpeg"
        };
        object attachment = isPdf
            ? new { type = "document", source = new { type = "base64", media_type = mediaType, data = Convert.ToBase64String(data) } }
            : new { type = "image", source = new { type = "base64", media_type = mediaType, data = Convert.ToBase64String(data) } };

        var body = new
        {
            model = _o.AnthropicModel,
            max_tokens = maxTokens,
            messages = new object[]
            {
                new { role = "user", content = new object[] { attachment, new { type = "text", text = prompt } } }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", _o.AnthropicApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Anthropic API {Status}: {Body}", res.StatusCode, json);
            return null;
        }
        using var doc = JsonDocument.Parse(json);
        return string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(c => c.GetProperty("type").GetString() == "text")
            .Select(c => c.GetProperty("text").GetString()));
    }
}

public class NoAiExtractor : IAiExtractor
{
    public bool Enabled => false;
    public Task<(AiExtraction? Result, string? Raw)> ExtractAsync(string filePath, string contentType, byte[] data,
        IReadOnlyList<string> categories, CancellationToken ct) => Task.FromResult<(AiExtraction?, string?)>((null, null));
    public Task<string?> CompleteAsync(string instructions, string filePath, string contentType, byte[] data, int maxTokens, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
