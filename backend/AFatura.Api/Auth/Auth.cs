using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AFatura.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "a-fatura";
    public string Audience { get; set; } = "a-fatura";
    public string Key { get; set; } = "";
    public int ExpiryHours { get; set; } = 24 * 7;
}

public class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public string Create(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.Name),
            new("role", user.PlatformRole.ToString()),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims,
            expires: DateTime.UtcNow.AddHours(_o.ExpiryHours), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class CurrentUser(IHttpContextAccessor http)
{
    private ClaimsPrincipal? P => http.HttpContext?.User;

    public Guid Id => Guid.TryParse(P?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? P?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new AppException(401, "Não autenticado");

    public PlatformRole Role => Enum.TryParse<PlatformRole>(P?.FindFirstValue("role") ?? P?.FindFirstValue(ClaimTypes.Role), out var r) ? r : PlatformRole.User;
    public bool IsAdmin => Role == PlatformRole.Admin;
}

/// <summary>Effective rights of the current user on one organization.</summary>
public record OrgAccess(Guid OrganizationId, Guid UserId, MemberRole? MemberRole, bool IsAccountant, bool IsAdmin)
{
    /// <summary>Managers, the assigned accountant and the backoffice see every document of the organization.</summary>
    public bool CanSeeAll => IsAdmin || IsAccountant || MemberRole == Domain.MemberRole.Manager;
    /// <summary>Managing collaborators, vehicles, bank accounts, categories.</summary>
    public bool CanManage => IsAdmin || MemberRole == Domain.MemberRole.Manager;
    public bool IsMember => MemberRole != null;
    public bool CanUpload => IsMember || IsAccountant || IsAdmin;
}

public class AccessService(AppDbContext db, CurrentUser me)
{
    public async Task<OrgAccess> GetAsync(Guid orgId, CancellationToken ct = default)
    {
        var userId = me.Id;
        if (me.IsAdmin) return new OrgAccess(orgId, userId, null, false, true);

        var membership = await db.Memberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.UserId == userId && m.IsActive)
            .Select(m => (MemberRole?)m.Role).FirstOrDefaultAsync(ct);
        var isAccountant = me.Role == PlatformRole.Accountant &&
            await db.AccountantAssignments.AnyAsync(a => a.OrganizationId == orgId && a.AccountantUserId == userId, ct);

        if (membership == null && !isAccountant) throw new AppException(403, "Sem acesso a esta entidade");
        return new OrgAccess(orgId, userId, membership, isAccountant, false);
    }

    public async Task<OrgAccess> RequireManageAsync(Guid orgId, CancellationToken ct = default)
    {
        var a = await GetAsync(orgId, ct);
        if (!a.CanManage) throw new AppException(403, "Apenas o gestor da entidade pode fazer esta operação");
        return a;
    }

    public async Task<OrgAccess> RequireSeeAllAsync(Guid orgId, CancellationToken ct = default)
    {
        var a = await GetAsync(orgId, ct);
        if (!a.CanSeeAll) throw new AppException(403, "Sem permissões para esta consulta");
        return a;
    }

    public async Task<OrgAccess> RequireAccountantAsync(Guid orgId, CancellationToken ct = default)
    {
        var a = await GetAsync(orgId, ct);
        if (!a.IsAccountant && !a.IsAdmin) throw new AppException(403, "Apenas o contabilista");
        return a;
    }

    public IQueryable<Invoice> Invoices(OrgAccess a) =>
        a.CanSeeAll
            ? db.Invoices.Where(i => i.OrganizationId == a.OrganizationId)
            : db.Invoices.Where(i => i.OrganizationId == a.OrganizationId && i.UploadedByUserId == a.UserId);

    /// <summary>Clients may not change documents in a month the accountant has closed.</summary>
    public async Task EnsurePeriodOpenAsync(OrgAccess a, DateOnly? date, CancellationToken ct = default)
    {
        if (date == null || a.IsAccountant || a.IsAdmin) return;
        var closed = await db.PeriodCloses.AnyAsync(p => p.OrganizationId == a.OrganizationId
            && p.Year == date.Value.Year && p.Month == date.Value.Month, ct);
        if (closed) throw new AppException(409, $"O período {date.Value:MM/yyyy} já foi fechado pelo contabilista");
    }
}
