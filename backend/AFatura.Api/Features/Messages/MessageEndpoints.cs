using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Messages;

public record MessageInput(string Subject, string Body, Guid? ParentId, Guid? RelatedInvoiceId);

/// <summary>Each client (organization) has an inbox. The accountant/backoffice writes to it; the client replies in the thread.</summary>
public static class MessageEndpoints
{
    public static void MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orgs/{orgId:guid}/messages").RequireAuthorization().WithTags("Messages");

        g.MapGet("", async (Guid orgId, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            await access.GetAsync(orgId, ct);
            var userId = me.Id;
            var items = await db.Messages.Where(m => m.OrganizationId == orgId).OrderByDescending(m => m.CreatedAt).Take(500)
                .Select(m => new
                {
                    m.Id, m.ParentId, m.Subject, m.Body, m.FromAccountant, m.RelatedInvoiceId, m.CreatedAt,
                    SenderName = m.Sender.Name, m.SenderUserId,
                    Read = m.SenderUserId == userId || db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == userId)
                }).ToListAsync(ct);
            return Results.Ok(items);
        });

        g.MapPost("", async (Guid orgId, MessageInput input, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            if (string.IsNullOrWhiteSpace(input.Body)) throw new AppException(400, "Mensagem vazia");
            if (input.ParentId is { } pid && !await db.Messages.AnyAsync(m => m.Id == pid && m.OrganizationId == orgId, ct))
                throw new AppException(400, "Conversa inválida");
            var msg = new Message
            {
                OrganizationId = orgId, SenderUserId = me.Id, ParentId = input.ParentId,
                Subject = string.IsNullOrWhiteSpace(input.Subject) ? "(sem assunto)" : input.Subject.Trim(),
                Body = input.Body.Trim(), RelatedInvoiceId = input.RelatedInvoiceId,
                FromAccountant = a.IsAccountant || a.IsAdmin
            };
            db.Messages.Add(msg);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { msg.Id });
        });

        g.MapPost("read", async (Guid orgId, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            await access.GetAsync(orgId, ct);
            var userId = me.Id;
            var unread = await db.Messages.Where(m => m.OrganizationId == orgId && m.SenderUserId != userId
                                                       && !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == userId))
                .Select(m => m.Id).ToListAsync(ct);
            db.MessageReads.AddRange(unread.Select(id => new MessageRead { MessageId = id, UserId = userId }));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Unread counters for every organization the user can see (members and accountants).
        app.MapGet("/api/inbox", async (CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.Id;
            var orgIds = await db.Memberships.Where(m => m.UserId == userId && m.IsActive).Select(m => m.OrganizationId)
                .Union(db.AccountantAssignments.Where(a => a.AccountantUserId == userId).Select(a => a.OrganizationId))
                .ToListAsync(ct);
            var counts = await db.Messages.Where(m => orgIds.Contains(m.OrganizationId) && m.SenderUserId != userId
                                                      && !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == userId))
                .GroupBy(m => m.OrganizationId).Select(x => new { OrganizationId = x.Key, Unread = x.Count(), Last = x.Max(m => m.CreatedAt) })
                .ToListAsync(ct);
            return Results.Ok(new { total = counts.Sum(c => c.Unread), organizations = counts });
        }).RequireAuthorization().WithTags("Messages");
    }
}
