using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Auth;

public record RegisterInput(string Name, string Email, string Password, OrganizationType AccountType, string? OrganizationName, string? Nif);
public record LoginInput(string Email, string Password);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");
        var hasher = new PasswordHasher<User>();

        g.MapPost("register", async (RegisterInput input, AppDbContext db, TokenService tokens, IConfiguration config, CancellationToken ct) =>
        {
            if (config.GetValue("Auth:AllowSelfRegistration", true) == false) throw new AppException(403, "Registo desativado");
            var email = input.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw new AppException(400, "Email inválido");
            if (input.Password.Length < 8) throw new AppException(400, "A palavra-passe deve ter pelo menos 8 caracteres");
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new AppException(409, "Já existe uma conta com este email");
            if (!string.IsNullOrWhiteSpace(input.Nif) && !Text.IsValidPtNif(input.Nif)) throw new AppException(400, "NIF inválido");

            var user = new User { Email = email, Name = input.Name.Trim() };
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            var org = new Organization
            {
                Type = input.AccountType,
                Name = input.AccountType == OrganizationType.Individual ? (input.OrganizationName ?? input.Name).Trim() : (input.OrganizationName ?? "").Trim(),
                Nif = Text.NormalizeNif(input.Nif) is { Length: > 0 } n ? n : null,
                Email = email
            };
            if (string.IsNullOrWhiteSpace(org.Name)) throw new AppException(400, "Indique o nome da empresa");
            db.Users.Add(user);
            db.Organizations.Add(org);
            db.Memberships.Add(new Membership { UserId = user.Id, OrganizationId = org.Id, Role = MemberRole.Manager, MailboxToken = Text.RandomToken() });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { token = tokens.Create(user) });
        });

        g.MapPost("login", async (LoginInput input, AppDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            var email = input.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user == null || !user.IsActive || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
                throw new AppException(401, "Email ou palavra-passe incorretos");
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { token = tokens.Create(user), mustChangePassword = user.MustChangePassword });
        });

        g.MapGet("me", async (CurrentUser me, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me.Id, ct) ?? throw new AppException(401, "Sessão inválida");
            var domain = config["Mail:InboundDomain"];
            var memberships = await db.Memberships.AsNoTracking().Where(m => m.UserId == user.Id && m.IsActive && m.Organization.IsActive)
                .Select(m => new
                {
                    OrganizationId = m.OrganizationId, m.Organization.Name, m.Organization.Nif, Type = m.Organization.Type.ToString(),
                    Role = m.Role.ToString(), Mailbox = domain == null ? null : m.MailboxToken + "@" + domain
                }).ToListAsync(ct);
            var isAccountant = user.PlatformRole == PlatformRole.Accountant;
            var clients = await db.AccountantAssignments.AsNoTracking().Where(a => isAccountant && a.AccountantUserId == user.Id)
                .Select(a => new { OrganizationId = a.OrganizationId, a.Organization.Name, a.Organization.Nif, Type = a.Organization.Type.ToString() })
                .OrderBy(a => a.Name).ToListAsync(ct);
            return Results.Ok(new
            {
                user.Id, user.Email, user.Name, Role = user.PlatformRole.ToString(), user.MustChangePassword,
                memberships, clients
            });
        }).RequireAuthorization();

        g.MapPost("change-password", async (ChangePasswordInput input, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed)
                throw new AppException(400, "Palavra-passe atual incorreta");
            if (input.NewPassword.Length < 8) throw new AppException(400, "A palavra-passe deve ter pelo menos 8 caracteres");
            user.PasswordHash = hasher.HashPassword(user, input.NewPassword);
            user.MustChangePassword = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
