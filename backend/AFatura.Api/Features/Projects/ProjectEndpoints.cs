using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Projects;

public record ProjectInput(string Name, string? Code, string? ClientName, string? Description, string? Color,
    decimal? Budget, DateOnly? StartDate, DateOnly? EndDate, bool IsActive = true);

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orgs/{orgId:guid}/projects").RequireAuthorization().WithTags("Projects");

        // Everyone in the organization can list projects (to allocate their expenses); totals only for those who see all.
        g.MapGet("", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var projects = await db.Projects.Where(p => p.OrganizationId == orgId)
                .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync(ct);
            var seeAll = a.CanSeeAll;
            var stats = await db.Invoices.Where(i => seeAll && i.OrganizationId == orgId && i.ProjectId != null && i.DuplicateOfId == null)
                .GroupBy(i => i.ProjectId)
                .Select(x => new { ProjectId = x.Key, Spent = x.Sum(i => i.TotalAmount ?? 0), Vat = x.Sum(i => i.VatAmount ?? 0), Count = x.Count(), Last = x.Max(i => i.IssueDate) })
                .ToListAsync(ct);
            return Results.Ok(projects.Select(p =>
            {
                var st = stats.FirstOrDefault(s => s.ProjectId == p.Id);
                return new
                {
                    p.Id, p.Name, p.Code, p.ClientName, p.Description, p.Color, p.Budget, p.StartDate, p.EndDate, p.IsActive, p.CreatedAt,
                    Spent = seeAll ? st?.Spent ?? 0 : (decimal?)null,
                    Vat = seeAll ? st?.Vat ?? 0 : (decimal?)null,
                    Invoices = seeAll ? st?.Count ?? 0 : (int?)null,
                    LastExpense = st?.Last,
                    BudgetUsed = seeAll && p.Budget is > 0 ? Math.Round((st?.Spent ?? 0) / p.Budget.Value, 4) : (decimal?)null
                };
            }));
        });

        g.MapGet("{id:guid}", async (Guid orgId, Guid id, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireSeeAllAsync(orgId, ct);
            var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct) ?? throw new AppException(404, "Projeto não encontrado");
            var q = db.Invoices.Where(i => i.OrganizationId == orgId && i.ProjectId == id && i.DuplicateOfId == null);
            var byCategory = await q.GroupBy(i => new { Name = i.Category != null ? i.Category.Name : "Sem categoria", Color = i.Category != null ? i.Category.Color : "#cbd5e1" })
                .Select(x => new { x.Key.Name, x.Key.Color, Total = x.Sum(i => i.TotalAmount ?? 0), Count = x.Count() })
                .OrderByDescending(x => x.Total).ToListAsync(ct);
            var bySupplier = await q.GroupBy(i => i.SupplierName ?? i.SupplierNif ?? "—")
                .Select(x => new { Name = x.Key, Total = x.Sum(i => i.TotalAmount ?? 0), Count = x.Count() })
                .OrderByDescending(x => x.Total).Take(10).ToListAsync(ct);
            var byMonth = await q.Where(i => i.IssueDate != null)
                .GroupBy(i => new { i.IssueDate!.Value.Year, i.IssueDate!.Value.Month })
                .Select(x => new { x.Key.Year, x.Key.Month, Total = x.Sum(i => i.TotalAmount ?? 0) })
                .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct);
            var spent = byCategory.Sum(c => c.Total);
            return Results.Ok(new
            {
                Project = p, Spent = spent, Invoices = byCategory.Sum(c => c.Count),
                BudgetUsed = p.Budget is > 0 ? Math.Round(spent / p.Budget.Value, 4) : (decimal?)null,
                byCategory, bySupplier, byMonth
            });
        });

        g.MapPost("", async (Guid orgId, ProjectInput input, AccessService access, CurrentUser me, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            Validate(input);
            var p = new Project { OrganizationId = orgId };
            Apply(p, input);
            db.Projects.Add(p);
            audit.Log(orgId, me.Id, "project.create", nameof(Project), p.Id, p.Name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(p);
        });

        g.MapPut("{id:guid}", async (Guid orgId, Guid id, ProjectInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            Validate(input);
            var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct) ?? throw new AppException(404, "Projeto não encontrado");
            Apply(p, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(p);
        });

        g.MapDelete("{id:guid}", async (Guid orgId, Guid id, AccessService access, CurrentUser me, AuditService audit, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct) ?? throw new AppException(404, "Projeto não encontrado");
            if (await db.Invoices.AnyAsync(i => i.ProjectId == id, ct)) p.IsActive = false; // keep history of allocated expenses
            else db.Projects.Remove(p);
            audit.Log(orgId, me.Id, "project.delete", nameof(Project), p.Id, p.Name);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void Validate(ProjectInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Name)) throw new AppException(400, "Indique o nome do projeto");
        if (i.StartDate != null && i.EndDate != null && i.EndDate < i.StartDate) throw new AppException(400, "A data de fim é anterior ao início");
        if (i.Budget is < 0) throw new AppException(400, "Orçamento inválido");
    }

    private static void Apply(Project p, ProjectInput i)
    {
        p.Name = i.Name.Trim(); p.Code = i.Code?.Trim(); p.ClientName = i.ClientName?.Trim(); p.Description = i.Description;
        p.Color = string.IsNullOrWhiteSpace(i.Color) ? p.Color : i.Color; p.Budget = i.Budget;
        p.StartDate = i.StartDate; p.EndDate = i.EndDate; p.IsActive = i.IsActive;
    }
}
