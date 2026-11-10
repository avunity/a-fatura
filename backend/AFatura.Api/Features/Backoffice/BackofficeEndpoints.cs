using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Backoffice;

public record CreateUserInput(string Email, string Name, PlatformRole Role);
public record UpdateUserInput(bool? IsActive, PlatformRole? Role, bool ResetPassword = false);
public record AssignInput(Guid OrganizationId, Guid AccountantUserId);
public record OrgStatusInput(bool IsActive);

/// <summary>Platform backoffice (Admin role). Admins also have full access to every /api/orgs/{id} endpoint.</summary>
public static class BackofficeEndpoints
{
    public static void MapBackofficeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/backoffice").RequireAuthorization("Admin").WithTags("Backoffice");

        g.MapGet("stats", async (AppDbContext db, CancellationToken ct) =>
        {
            var since = DateTime.UtcNow.Date.AddDays(-30);
            return Results.Ok(new
            {
                Users = await db.Users.GroupBy(u => u.PlatformRole).Select(x => new { Role = x.Key.ToString(), Count = x.Count() }).ToListAsync(ct),
                Organizations = await db.Organizations.GroupBy(o => o.Type).Select(x => new { Type = x.Key.ToString(), Count = x.Count() }).ToListAsync(ct),
                Invoices = await db.Invoices.CountAsync(ct),
                ByStatus = await db.Invoices.GroupBy(i => i.Status).Select(x => new { Status = x.Key.ToString(), Count = x.Count() }).ToListAsync(ct),
                ByMethod = await db.Invoices.GroupBy(i => i.ExtractionMethod).Select(x => new { Method = x.Key.ToString(), Count = x.Count() }).ToListAsync(ct),
                BySource = await db.Invoices.GroupBy(i => i.Source).Select(x => new { Source = x.Key.ToString(), Count = x.Count() }).ToListAsync(ct),
                Daily = await db.Invoices.Where(i => i.CreatedAt >= since).GroupBy(i => i.CreatedAt.Date)
                    .Select(x => new { Day = x.Key, Count = x.Count() }).OrderBy(x => x.Day).ToListAsync(ct),
                StorageBytes = await db.Files.SumAsync(f => (long?)f.Size, ct) ?? 0
            });
        });

        g.MapGet("organizations", async (string? q, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Organizations.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(o => EF.Functions.ILike(o.Name, $"%{q}%") || (o.Nif != null && o.Nif.Contains(q)));
            return Results.Ok(await query.OrderBy(o => o.Name).Take(500).Select(o => new
            {
                o.Id, o.Name, o.Nif, Type = o.Type.ToString(), o.IsActive, o.CreatedAt,
                Members = o.Members.Count,
                Invoices = db.Invoices.Count(i => i.OrganizationId == o.Id),
                LastUpload = db.Invoices.Where(i => i.OrganizationId == o.Id).Max(i => (DateTime?)i.CreatedAt),
                Accountants = o.Accountants.Select(a => new { a.AccountantUserId, a.AccountantUser.Name })
            }).ToListAsync(ct));
        });

        g.MapPut("organizations/{id:guid}", async (Guid id, OrgStatusInput input, AppDbContext db, CancellationToken ct) =>
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new AppException(404, "Entidade não encontrada");
            org.IsActive = input.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("users", async (string? q, PlatformRole? role, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Users.AsQueryable();
            if (role is { } r) query = query.Where(u => u.PlatformRole == r);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => EF.Functions.ILike(u.Name, $"%{q}%") || EF.Functions.ILike(u.Email, $"%{q}%"));
            return Results.Ok(await query.OrderBy(u => u.Name).Take(500).Select(u => new
            {
                u.Id, u.Email, u.Name, Role = u.PlatformRole.ToString(), u.IsActive, u.CreatedAt, u.LastLoginAt,
                Organizations = u.Memberships.Select(m => new { m.OrganizationId, m.Organization.Name, Role = m.Role.ToString() }),
                Clients = db.AccountantAssignments.Count(a => a.AccountantUserId == u.Id)
            }).ToListAsync(ct));
        });

        g.MapPost("users", async (CreateUserInput input, AppDbContext db, CancellationToken ct) =>
        {
            var email = input.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new AppException(409, "Email já registado");
            var temp = Text.RandomPassword();
            var user = new User { Email = email, Name = input.Name, PlatformRole = input.Role, MustChangePassword = true };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, temp);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user.Id, user.Email, tempPassword = temp });
        });

        g.MapPut("users/{id:guid}", async (Guid id, UpdateUserInput input, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new AppException(404, "Utilizador não encontrado");
            if (input.IsActive is { } a) user.IsActive = a;
            if (input.Role is { } r) user.PlatformRole = r;
            string? temp = null;
            if (input.ResetPassword)
            {
                temp = Text.RandomPassword();
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, temp);
                user.MustChangePassword = true;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { tempPassword = temp });
        });

        g.MapPost("assignments", async (AssignInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Users.AnyAsync(u => u.Id == input.AccountantUserId && u.PlatformRole == PlatformRole.Accountant, ct))
                throw new AppException(400, "O utilizador não é contabilista");
            if (!await db.AccountantAssignments.AnyAsync(a => a.AccountantUserId == input.AccountantUserId && a.OrganizationId == input.OrganizationId, ct))
                db.AccountantAssignments.Add(new AccountantAssignment { AccountantUserId = input.AccountantUserId, OrganizationId = input.OrganizationId });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("assignments", async (Guid organizationId, Guid accountantUserId, AppDbContext db, CancellationToken ct) =>
        {
            await db.AccountantAssignments.Where(a => a.AccountantUserId == accountantUserId && a.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("audit", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.AuditLogs.OrderByDescending(l => l.CreatedAt).Take(1000).ToListAsync(ct)));
    }
}
