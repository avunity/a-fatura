using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Organizations;

public record OrganizationUpdate(string Name, string? Nif, string? Address, string? Email, int? SubmissionDeadlineDay);
public record AddMemberInput(string Email, string Name, MemberRole Role);
public record UpdateMemberInput(MemberRole? Role, bool? IsActive);
public record AssignAccountantInput(string Email);

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orgs/{orgId:guid}").RequireAuthorization().WithTags("Organizations");

        g.MapGet("", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var org = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, ct);
            var activeMembers = await db.Memberships.CountAsync(m => m.OrganizationId == orgId && m.IsActive && m.User.IsActive, ct);
            var accountants = await db.AccountantAssignments.Where(x => x.OrganizationId == orgId)
                .Select(x => new { x.AccountantUserId, x.AccountantUser.Name, x.AccountantUser.Email }).ToListAsync(ct);
            return Results.Ok(new
            {
                org.Id, org.Name, org.Nif, org.Address, org.Email, Type = org.Type.ToString(), org.SubmissionDeadlineDay,
                Access = new { a.CanSeeAll, a.CanManage, a.IsAccountant, a.IsAdmin, Role = a.MemberRole?.ToString() },
                activeMembers,
                accountants
            });
        });

        g.MapPut("", async (Guid orgId, OrganizationUpdate u, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            if (!string.IsNullOrWhiteSpace(u.Nif) && !Text.IsValidPtNif(u.Nif)) throw new AppException(400, "NIF inválido");
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
            org.Name = u.Name.Trim();
            org.Nif = Text.NormalizeNif(u.Nif) is { Length: > 0 } n ? n : null;
            org.Address = u.Address;
            org.Email = u.Email;
            if (u.SubmissionDeadlineDay is >= 1 and <= 28) org.SubmissionDeadlineDay = u.SubmissionDeadlineDay.Value;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ── Collaborators ──
        g.MapGet("members", async (Guid orgId, AccessService access, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var domain = config["Mail:InboundDomain"];
            var stats = await db.Invoices.Where(i => i.OrganizationId == orgId)
                .GroupBy(i => i.UploadedByUserId).Select(x => new { UserId = x.Key, Count = x.Count(), Last = x.Max(i => i.CreatedAt) })
                .ToListAsync(ct);
            var members = await db.Memberships.Where(m => m.OrganizationId == orgId).Include(m => m.User).OrderBy(m => m.User.Name).ToListAsync(ct);
            return Results.Ok(members.Select(m => new
            {
                m.Id, m.UserId, m.User.Name, m.User.Email, Role = m.Role.ToString(), m.IsActive, m.CreatedAt,
                Mailbox = domain == null ? null : $"{m.MailboxToken}@{domain}",
                Invoices = stats.FirstOrDefault(s => s.UserId == m.UserId)?.Count ?? 0,
                LastUpload = stats.FirstOrDefault(s => s.UserId == m.UserId)?.Last
            }));
        });

        g.MapPost("members", async (Guid orgId, AddMemberInput input, AccessService access, CurrentUser me, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
            if (org.Type == OrganizationType.Individual) throw new AppException(400, "Contas individuais não têm colaboradores");

            var email = input.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            string? tempPassword = null;
            if (user == null)
            {
                tempPassword = Text.RandomPassword();
                user = new User { Email = email, Name = input.Name.Trim(), MustChangePassword = true };
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, tempPassword);
                db.Users.Add(user);
            }
            else if (await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.OrganizationId == orgId, ct))
                throw new AppException(409, "Este utilizador já pertence à empresa");

            var membership = new Membership { UserId = user.Id, OrganizationId = orgId, Role = input.Role, MailboxToken = Text.RandomToken() };
            db.Memberships.Add(membership);
            audit.Log(orgId, me.Id, "member.add", nameof(Membership), membership.Id, email);
            await db.SaveChangesAsync(ct);
            // The temporary password is shown once to the manager, who passes it to the collaborator.
            return Results.Ok(new { membership.Id, user.Email, tempPassword });
        });

        g.MapPut("members/{membershipId:guid}", async (Guid orgId, Guid membershipId, UpdateMemberInput input, AccessService access,
            CurrentUser me, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId && x.OrganizationId == orgId, ct)
                    ?? throw new AppException(404, "Colaborador não encontrado");
            if (m.UserId == me.Id && (input.Role == MemberRole.Collaborator || input.IsActive == false))
                throw new AppException(400, "Não pode retirar as suas próprias permissões de gestor");
            if (input.Role is { } role) m.Role = role;
            if (input.IsActive is { } active) m.IsActive = active;
            audit.Log(orgId, me.Id, "member.update", nameof(Membership), m.Id, $"{input.Role} {input.IsActive}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("members/{membershipId:guid}/rotate-mailbox", async (Guid orgId, Guid membershipId, AccessService access, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId && x.OrganizationId == orgId, ct)
                    ?? throw new AppException(404, "Colaborador não encontrado");
            if (!a.CanManage && m.UserId != me.Id) throw new AppException(403, "Sem permissão");
            m.MailboxToken = Text.RandomToken();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { m.MailboxToken });
        });

        // ── Accountant ──
        g.MapPost("accountant", async (Guid orgId, AssignAccountantInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var email = input.Email.Trim().ToLowerInvariant();
            var acc = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.PlatformRole == PlatformRole.Accountant, ct)
                      ?? throw new AppException(404, "Não existe nenhum contabilista registado com este email");
            if (!await db.AccountantAssignments.AnyAsync(x => x.OrganizationId == orgId && x.AccountantUserId == acc.Id, ct))
                db.AccountantAssignments.Add(new AccountantAssignment { OrganizationId = orgId, AccountantUserId = acc.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("accountant/{userId:guid}", async (Guid orgId, Guid userId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            await db.AccountantAssignments.Where(x => x.OrganizationId == orgId && x.AccountantUserId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("audit", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            return Results.Ok(await db.AuditLogs.Where(l => l.OrganizationId == orgId).OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync(ct));
        });
    }
}
