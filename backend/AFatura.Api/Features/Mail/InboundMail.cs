using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using AFatura.Api.Features.Invoices;
using AFatura.Api.Storage;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace AFatura.Api.Features.Mail;

public class MailOptions
{
    /// <summary>Domain of the per-collaborator addresses, e.g. in.fatura.avunity.com → a1b2c3@in.fatura.avunity.com</summary>
    public string? InboundDomain { get; set; }
    /// <summary>Shared secret for the inbound webhook (?key=...).</summary>
    public string? WebhookSecret { get; set; }
    public ImapOptions Imap { get; set; } = new();
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>Built-in SMTP receiver: the MX of the inbound domain points to the VPS and mail is delivered straight to the API.</summary>
public class SmtpOptions
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 2525;
    public string ServerName { get; set; } = "localhost";
    public int MaxMessageBytes { get; set; } = 30 * 1024 * 1024;
}

public class ImapOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Folder { get; set; } = "INBOX";
    public string ProcessedFolder { get; set; } = "Processed";
    public int PollSeconds { get; set; } = 60;
}

/// <summary>
/// Turns an e-mail into invoices: the recipient's local part identifies the collaborator (membership mailbox token),
/// each PDF/image attachment becomes a document. Works with a catch-all IMAP mailbox or any provider webhook that posts raw MIME.
/// </summary>
public class InboundMailService(AppDbContext db, InvoiceIntake intake, Microsoft.Extensions.Options.IOptions<MailOptions> options, ILogger<InboundMailService> log)
{
    private readonly MailOptions _o = options.Value;

    /// <param name="envelopeRecipients">SMTP RCPT TO addresses (more reliable than headers for BCC/forwarded mail).</param>
    public async Task<int> ProcessAsync(MimeMessage message, CancellationToken ct, IEnumerable<string>? envelopeRecipients = null)
    {
        var domain = string.IsNullOrWhiteSpace(_o.InboundDomain) ? null : _o.InboundDomain.Trim().ToLowerInvariant();
        var recipients = (envelopeRecipients ?? Enumerable.Empty<string>()).Select(a => MailboxAddress.TryParse(a, out var mb) ? mb : null).OfType<MailboxAddress>()
            .Concat(message.To.Mailboxes).Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes)
            .Concat(message.Headers.Where(h => h.Field is "Delivered-To" or "X-Original-To" or "Envelope-To")
                .SelectMany(h => InternetAddressList.TryParse(h.Value, out var l) ? l.Mailboxes : Enumerable.Empty<MailboxAddress>()))
            .Select(m => m.Address.ToLowerInvariant())
            .Where(a => domain == null || a.EndsWith("@" + domain))
            .Select(a => a.Split('@')[0].Split('+')[0])
            .Distinct().ToList();

        var memberships = await db.Memberships.Where(m => recipients.Contains(m.MailboxToken) && m.IsActive).ToListAsync(ct);
        if (memberships.Count == 0)
        {
            log.LogWarning("Inbound mail '{Subject}' from {From}: no matching mailbox ({To})", message.Subject, message.From, string.Join(",", recipients));
            return 0;
        }

        var attachments = message.BodyParts.OfType<MimePart>()
            .Where(p => p.Content != null && (p.IsAttachment || p.ContentType.MediaType is "image" || p.ContentType.MimeType == "application/pdf"))
            .Where(p => FileStorage.AllowedInvoiceTypes.Contains(p.ContentType.MimeType)
                        || FileStorage.AllowedInvoiceTypes.Contains(FileStorage.GuessContentType(p.FileName ?? "")))
            .Where(p => !(p.ContentType.MediaType == "image" && p.ContentDisposition?.Disposition == ContentDisposition.Inline && p.FileName == null)) // signatures/logos
            .ToList();

        var created = 0;
        foreach (var m in memberships)
        {
            foreach (var part in attachments)
            {
                using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms, ct);
                if (ms.Length < 2048 && part.ContentType.MediaType == "image") continue; // tiny tracking pixels/icons
                var name = part.FileName ?? $"email-{DateTime.UtcNow:yyyyMMddHHmmss}{(part.ContentType.MimeType == "application/pdf" ? ".pdf" : ".jpg")}";
                await intake.CreateAsync(m.OrganizationId, m.UserId, InvoiceSource.Email, name, part.ContentType.MimeType, ms.ToArray(),
                    notes: $"Recebido por email de {message.From} — {message.Subject}", ct: ct);
                created++;
            }
        }
        log.LogInformation("Inbound mail '{Subject}': {Count} documents created", message.Subject, created);
        return created;
    }
}

public static class MailEndpoints
{
    public static void MapMailEndpoints(this IEndpointRouteBuilder app)
    {
        // Accepts raw MIME (message/rfc822 body) or multipart with a raw MIME field — compatible with
        // Cloudflare Email Workers, Mailgun (body-mime), SendGrid Inbound Parse (email, "send raw"), Postfix pipe + curl.
        app.MapPost("/api/inbound/email", async (HttpRequest req, string? key, InboundMailService svc,
            Microsoft.Extensions.Options.IOptions<MailOptions> options, CancellationToken ct) =>
        {
            var secret = options.Value.WebhookSecret;
            if (string.IsNullOrEmpty(secret) || key != secret) return Results.Unauthorized();

            Stream raw;
            if (req.HasFormContentType)
            {
                var form = await req.ReadFormAsync(ct);
                var mime = form["body-mime"].FirstOrDefault() ?? form["email"].FirstOrDefault() ?? form["raw"].FirstOrDefault();
                if (mime != null) raw = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(mime));
                else if (form.Files.FirstOrDefault() is { } f) raw = f.OpenReadStream();
                else return Results.BadRequest("No MIME content");
            }
            else
            {
                var ms = new MemoryStream();
                await req.Body.CopyToAsync(ms, ct);
                ms.Position = 0;
                raw = ms;
            }

            var message = await MimeMessage.LoadAsync(raw, ct);
            var count = await svc.ProcessAsync(message, ct);
            return Results.Ok(new { documents = count });
        }).DisableAntiforgery().WithTags("Inbound");
    }
}

/// <summary>Polls a catch-all IMAP mailbox (e.g. *@in.fatura.avunity.com) and processes unseen messages.</summary>
public class ImapPollingWorker(IServiceScopeFactory scopes, Microsoft.Extensions.Options.IOptions<MailOptions> options, ILogger<ImapPollingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value.Imap;
        if (!o.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = new ImapClient();
                await client.ConnectAsync(o.Host, o.Port, o.UseSsl, stoppingToken);
                await client.AuthenticateAsync(o.User, o.Password, stoppingToken);
                var inbox = await client.GetFolderAsync(o.Folder, stoppingToken);
                await inbox.OpenAsync(FolderAccess.ReadWrite, stoppingToken);

                IMailFolder? processed = null;
                try { processed = await client.GetFolderAsync(o.ProcessedFolder, stoppingToken); }
                catch (FolderNotFoundException)
                {
                    var root = client.GetFolder(client.PersonalNamespaces[0]);
                    processed = await root.CreateAsync(o.ProcessedFolder, true, stoppingToken);
                }

                var uids = await inbox.SearchAsync(SearchQuery.NotSeen, stoppingToken);
                foreach (var uid in uids)
                {
                    var msg = await inbox.GetMessageAsync(uid, stoppingToken);
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<InboundMailService>().ProcessAsync(msg, stoppingToken);
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, stoppingToken);
                    if (processed != null) await inbox.MoveToAsync(uid, processed, stoppingToken);
                }
                await client.DisconnectAsync(true, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "IMAP polling failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, o.PollSeconds)), stoppingToken);
        }
    }
}
