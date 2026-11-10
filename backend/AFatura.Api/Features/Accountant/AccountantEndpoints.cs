using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Accountant;

public record BroadcastInput(List<Guid> OrganizationIds, string Subject, string Body);
public record RequestUpdate(DocumentRequestStatus Status);
public record ManualRequestInput(string Description);

public static class AccountantEndpoints
{
    public static void MapAccountantEndpoints(this IEndpointRouteBuilder app)
    {
        var acc = app.MapGroup("/api/accountant").RequireAuthorization().WithTags("Accountant");

        // Monthly health of every client: uploads, e-Fatura coverage, items to review, timeliness → score & ranking.
        acc.MapGet("dashboard", async (int year, int month, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            if (me.Role != PlatformRole.Accountant && !me.IsAdmin) throw new AppException(403, "Apenas contabilistas");
            var userId = me.Id;
            var orgs = await (me.IsAdmin
                    ? db.Organizations.Where(o => o.IsActive)
                    : db.AccountantAssignments.Where(a => a.AccountantUserId == userId).Select(a => a.Organization))
                .Select(o => new { o.Id, o.Name, o.Nif, o.Type, o.SubmissionDeadlineDay }).ToListAsync(ct);
            var ids = orgs.Select(o => o.Id).ToList();

            var invRows = await db.Invoices.Where(i => ids.Contains(i.OrganizationId) && i.DuplicateOfId == null
                                                   && i.IssueDate != null && i.IssueDate.Value.Year == year && i.IssueDate.Value.Month == month)
                .Select(i => new { i.OrganizationId, i.TotalAmount, i.VatAmount, i.Status, Collected = i.EFaturaRecordId != null, i.CreatedAt })
                .ToListAsync(ct);
            var inv = invRows.GroupBy(i => i.OrganizationId)
                .Select(g => new
                {
                    OrgId = g.Key, Count = g.Count(), Total = g.Sum(i => i.TotalAmount ?? 0), Vat = g.Sum(i => i.VatAmount ?? 0),
                    Review = g.Count(i => i.Status == InvoiceStatus.NeedsReview || i.Status == InvoiceStatus.Failed),
                    Collected = g.Count(i => i.Collected),
                    CreatedDates = g.Select(i => i.CreatedAt).ToList()
                }).ToList();
            var ef = await db.EFaturaRecords.Where(r => ids.Contains(r.OrganizationId) && r.IssueDate.Year == year && r.IssueDate.Month == month)
                .GroupBy(r => r.OrganizationId)
                .Select(g => new { OrgId = g.Key, Count = g.Count(), Matched = g.Count(r => r.MatchedInvoiceId != null), Total = g.Sum(r => r.Total) })
                .ToListAsync(ct);
            var lastUpload = await db.Invoices.Where(i => ids.Contains(i.OrganizationId))
                .GroupBy(i => i.OrganizationId).Select(g => new { OrgId = g.Key, Last = g.Max(i => i.CreatedAt) }).ToListAsync(ct);
            var lastEf = await db.EFaturaImports.Where(i => ids.Contains(i.OrganizationId))
                .GroupBy(i => i.OrganizationId).Select(g => new { OrgId = g.Key, Last = g.Max(i => i.CreatedAt), Covered = g.Max(i => i.PeriodEnd) }).ToListAsync(ct);
            var requests = await db.DocumentRequests.Where(d => ids.Contains(d.OrganizationId) && d.Status == DocumentRequestStatus.Open)
                .GroupBy(d => d.OrganizationId).Select(g => new { OrgId = g.Key, Count = g.Count() }).ToListAsync(ct);
            var unread = await db.Messages.Where(m => ids.Contains(m.OrganizationId) && !m.FromAccountant
                                                      && !db.MessageReads.Any(r => r.MessageId == m.Id && r.UserId == userId))
                .GroupBy(m => m.OrganizationId).Select(g => new { OrgId = g.Key, Count = g.Count() }).ToListAsync(ct);
            var closed = await db.PeriodCloses.Where(p => ids.Contains(p.OrganizationId) && p.Year == year && p.Month == month)
                .Select(p => p.OrganizationId).ToListAsync(ct);

            var rows = orgs.Select(o =>
            {
                var i = inv.FirstOrDefault(x => x.OrgId == o.Id);
                var e = ef.FirstOrDefault(x => x.OrgId == o.Id);
                var deadline = new DateTime(year, month, 1, 23, 59, 59, DateTimeKind.Utc).AddMonths(1).AddDays(o.SubmissionDeadlineDay - 1);
                var onTime = i == null || i.Count == 0 ? (decimal?)null : (decimal)i.CreatedDates.Count(d => d <= deadline) / i.Count;
                decimal? coverage = e == null || e.Count == 0 ? null : (decimal)e.Matched / e.Count;
                var reviewed = i == null || i.Count == 0 ? (decimal?)null : 1 - (decimal)i.Review / i.Count;

                // Weighted score; components without data are left out and weights re-normalized.
                var parts = new List<(decimal Value, decimal Weight)>();
                if (coverage != null) parts.Add((coverage.Value, 0.5m));
                if (reviewed != null) parts.Add((reviewed.Value, 0.25m));
                if (onTime != null) parts.Add((onTime.Value, 0.25m));
                decimal? score = parts.Count == 0 ? null : Math.Round(100 * parts.Sum(p => p.Value * p.Weight) / parts.Sum(p => p.Weight), 0);
                if (i == null && e is { Count: > 0 }) score = 0; // AT has invoices but client sent nothing

                return new
                {
                    OrganizationId = o.Id, o.Name, o.Nif, Type = o.Type.ToString(),
                    Invoices = i?.Count ?? 0, Total = i?.Total ?? 0, Vat = i?.Vat ?? 0, NeedsReview = i?.Review ?? 0,
                    EFaturaRecords = e?.Count ?? 0, EFaturaMatched = e?.Matched ?? 0, EFaturaTotal = e?.Total ?? 0,
                    Missing = (e?.Count ?? 0) - (e?.Matched ?? 0),
                    Coverage = coverage, OnTime = onTime, Score = score,
                    LastUpload = lastUpload.FirstOrDefault(x => x.OrgId == o.Id)?.Last,
                    LastEFaturaImport = lastEf.FirstOrDefault(x => x.OrgId == o.Id)?.Last,
                    EFaturaCoveredUntil = lastEf.FirstOrDefault(x => x.OrgId == o.Id)?.Covered,
                    OpenRequests = requests.FirstOrDefault(x => x.OrgId == o.Id)?.Count ?? 0,
                    UnreadMessages = unread.FirstOrDefault(x => x.OrgId == o.Id)?.Count ?? 0,
                    PeriodClosed = closed.Contains(o.Id)
                };
            }).OrderByDescending(r => r.Score ?? -1).ToList();

            var scored = rows.Where(r => r.Score != null).ToList();
            return Results.Ok(new
            {
                year, month,
                totals = new
                {
                    Clients = rows.Count, Invoices = rows.Sum(r => r.Invoices), Missing = rows.Sum(r => r.Missing),
                    NeedsReview = rows.Sum(r => r.NeedsReview), Closed = rows.Count(r => r.PeriodClosed),
                    NoUploads = rows.Count(r => r.Invoices == 0)
                },
                best = scored.Take(5),
                worst = scored.OrderBy(r => r.Score).Take(5),
                clients = rows
            });
        });

        acc.MapPost("broadcast", async (BroadcastInput input, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            if (me.Role != PlatformRole.Accountant && !me.IsAdmin) throw new AppException(403, "Apenas contabilistas");
            var userId = me.Id;
            var allowed = me.IsAdmin
                ? input.OrganizationIds
                : await db.AccountantAssignments.Where(a => a.AccountantUserId == userId && input.OrganizationIds.Contains(a.OrganizationId))
                    .Select(a => a.OrganizationId).ToListAsync(ct);
            db.Messages.AddRange(allowed.Select(orgId => new Message
            {
                OrganizationId = orgId, SenderUserId = userId, FromAccountant = true, Subject = input.Subject, Body = input.Body
            }));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { sent = allowed.Count });
        });

        // ── Period close (per organization) ──
        var org = app.MapGroup("/api/orgs/{orgId:guid}").RequireAuthorization().WithTags("Accountant");

        org.MapGet("periods", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.GetAsync(orgId, ct);
            return Results.Ok(await db.PeriodCloses.Where(p => p.OrganizationId == orgId).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync(ct));
        });

        org.MapPost("periods/{year:int}/{month:int}/close", async (Guid orgId, int year, int month, AccessService access, CurrentUser me,
            AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireAccountantAsync(orgId, ct);
            if (!await db.PeriodCloses.AnyAsync(p => p.OrganizationId == orgId && p.Year == year && p.Month == month, ct))
            {
                db.PeriodCloses.Add(new PeriodClose { OrganizationId = orgId, Year = year, Month = month, ClosedByUserId = me.Id });
                audit.Log(orgId, me.Id, "period.close", data: $"{year}-{month:00}");
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });

        org.MapDelete("periods/{year:int}/{month:int}/close", async (Guid orgId, int year, int month, AccessService access, CurrentUser me,
            AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireAccountantAsync(orgId, ct);
            await db.PeriodCloses.Where(p => p.OrganizationId == orgId && p.Year == year && p.Month == month).ExecuteDeleteAsync(ct);
            audit.Log(orgId, me.Id, "period.reopen", data: $"{year}-{month:00}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ── Document requests ──
        org.MapGet("requests", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.GetAsync(orgId, ct);
            return Results.Ok(await db.DocumentRequests.Where(d => d.OrganizationId == orgId).OrderByDescending(d => d.CreatedAt).Take(500).ToListAsync(ct));
        });

        org.MapPost("requests", async (Guid orgId, ManualRequestInput input, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireAccountantAsync(orgId, ct);
            db.DocumentRequests.Add(new DocumentRequest { OrganizationId = orgId, RequestedByUserId = me.Id, Description = input.Description });
            db.Messages.Add(new Message { OrganizationId = orgId, SenderUserId = me.Id, FromAccountant = true, Subject = "Pedido de documento", Body = input.Description });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        org.MapPut("requests/{id:guid}", async (Guid orgId, Guid id, RequestUpdate input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var r = await db.DocumentRequests.FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == orgId, ct) ?? throw new AppException(404, "Pedido não encontrado");
            r.Status = input.Status;
            r.ResolvedAt = input.Status == DocumentRequestStatus.Open ? null : DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
