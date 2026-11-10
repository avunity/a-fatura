using System.Buffers;
using AFatura.Api.Data;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using SmtpServerHost = SmtpServer.SmtpServer;

namespace AFatura.Api.Features.Mail;

/// <summary>
/// Receives mail for the inbound domain directly over SMTP (MX → VPS). Only accepts messages addressed to an
/// existing, active collaborator mailbox; everything else is rejected with 550, so it never relays nor stores spam.
/// </summary>
public class AFaturaMessageStore(IServiceScopeFactory scopes, Microsoft.Extensions.Options.IOptions<MailOptions> options, ILogger<AFaturaMessageStore> log) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        var domain = options.Value.InboundDomain?.Trim().ToLowerInvariant();
        var rcpts = transaction.To.Select(m => $"{m.User}@{m.Host}".ToLowerInvariant())
            .Where(a => string.IsNullOrEmpty(domain) || a.EndsWith("@" + domain)).ToList();
        var tokens = rcpts.Select(a => a.Split('@')[0].Split('+')[0]).Distinct().ToList();

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (tokens.Count == 0 || !await db.Memberships.AnyAsync(m => tokens.Contains(m.MailboxToken) && m.IsActive, cancellationToken))
        {
            log.LogInformation("SMTP: rejected message for {Rcpt}", string.Join(",", transaction.To.Select(m => $"{m.User}@{m.Host}")));
            return new SmtpResponse(SmtpReplyCode.MailboxUnavailable, "Mailbox unavailable");
        }

        try
        {
            using var ms = new MemoryStream();
            foreach (var segment in buffer) await ms.WriteAsync(segment, cancellationToken);
            ms.Position = 0;
            var message = await MimeMessage.LoadAsync(ms, cancellationToken);
            var count = await scope.ServiceProvider.GetRequiredService<InboundMailService>().ProcessAsync(message, cancellationToken, rcpts);
            log.LogInformation("SMTP: '{Subject}' → {Count} documents", message.Subject, count);
            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "SMTP: failed to process message");
            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Processing failed");
        }
    }
}

public class SmtpReceiverWorker(AFaturaMessageStore store, Microsoft.Extensions.Options.IOptions<MailOptions> options, ILogger<SmtpReceiverWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value.Smtp;
        if (!o.Enabled) return;

        var smtpOptions = new SmtpServerOptionsBuilder()
            .ServerName(o.ServerName)
            .Endpoint(b => b.Port(o.Port))
            .MaxMessageSize(o.MaxMessageBytes)
            .Build();

        var services = new SmtpServer.ComponentModel.ServiceProvider();
        services.Add(store);

        log.LogInformation("SMTP receiver listening on port {Port} as {Name}", o.Port, o.ServerName);
        await new SmtpServerHost(smtpOptions, services).StartAsync(stoppingToken);
    }
}
