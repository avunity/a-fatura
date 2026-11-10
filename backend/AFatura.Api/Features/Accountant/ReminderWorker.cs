using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Reminders;

/// <summary>
/// Once a day: three days before each client's submission deadline, posts a reminder in the client's inbox
/// when the previous month has no documents yet. Sent on behalf of the assigned accountant.
/// </summary>
public class ReminderWorker(IServiceScopeFactory scopes, ILogger<ReminderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Reminder job failed");
            }
            var now = DateTime.UtcNow;
            var next = now.Date.AddDays(1).AddHours(8); // 08:00 UTC ≈ Lisbon morning
            await Task.Delay(next - now, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var prev = today.AddMonths(-1);

        var assignments = await db.AccountantAssignments
            .Where(a => a.Organization.IsActive && a.Organization.SubmissionDeadlineDay - 3 == today.Day)
            .Select(a => new { a.OrganizationId, a.AccountantUserId, a.Organization.SubmissionDeadlineDay })
            .ToListAsync(ct);

        foreach (var a in assignments.DistinctBy(x => x.OrganizationId))
        {
            var hasDocs = await db.Invoices.AnyAsync(i => i.OrganizationId == a.OrganizationId && i.IssueDate != null
                                                          && i.IssueDate.Value.Year == prev.Year && i.IssueDate.Value.Month == prev.Month, ct);
            var subject = $"Lembrete: documentos de {prev:MM/yyyy}";
            var alreadySent = await db.Messages.AnyAsync(m => m.OrganizationId == a.OrganizationId && m.Subject == subject, ct);
            if (hasDocs || alreadySent) continue;

            db.Messages.Add(new Message
            {
                OrganizationId = a.OrganizationId, SenderUserId = a.AccountantUserId, FromAccountant = true, Subject = subject,
                Body = $"Ainda não recebemos documentos de {prev:MM/yyyy}. Por favor carregue as faturas até ao dia {a.SubmissionDeadlineDay}."
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
