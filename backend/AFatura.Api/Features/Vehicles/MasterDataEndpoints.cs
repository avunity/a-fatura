using AFatura.Api.Auth;
using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Vehicles;

public record VehicleInput(string Plate, string? Make, string? Model, FuelType FuelType, DateOnly? AcquiredAt, decimal? AcquisitionValue,
    DateOnly? InsuranceRenewal, DateOnly? InspectionDue, string? Notes, bool IsActive = true);

public record CategoryInput(string Name, string? Color, string? Keywords, bool IsVehicleRelated, decimal? VatDeductiblePercent, bool IsActive = true);

public static class MasterDataEndpoints
{
    public static void MapMasterDataEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Vehicles (company manager or individual) ──
        var v = app.MapGroup("/api/orgs/{orgId:guid}/vehicles").RequireAuthorization().WithTags("Vehicles");

        v.MapGet("", async (Guid orgId, int? year, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var y = year ?? DateTime.UtcNow.Year;
            var vehicles = await db.Vehicles.Where(x => x.OrganizationId == orgId).OrderBy(x => x.Plate).ToListAsync(ct);
            // Cost per vehicle in the year, split by category (fuel, insurance, tolls, maintenance…)
            var seeAll = a.CanSeeAll; // collaborators don't see company-wide vehicle costs
            var costs = await db.Invoices.Where(i => seeAll && i.OrganizationId == orgId && i.VehicleId != null && i.DuplicateOfId == null
                                                     && i.IssueDate != null && i.IssueDate.Value.Year == y)
                .GroupBy(i => new { i.VehicleId, Category = i.Category != null ? i.Category.Name : "Sem categoria" })
                .Select(g => new { g.Key.VehicleId, g.Key.Category, Total = g.Sum(i => i.TotalAmount ?? 0), Liters = g.Sum(i => i.FuelLiters ?? 0), Kwh = g.Sum(i => i.EnergyKwh ?? 0) })
                .ToListAsync(ct);
            return Results.Ok(vehicles.Select(x => new
            {
                x.Id, x.Plate, x.Make, x.Model, FuelType = x.FuelType.ToString(), x.AcquiredAt, x.AcquisitionValue,
                x.InsuranceRenewal, x.InspectionDue, x.Notes, x.IsActive,
                Costs = costs.Where(c => c.VehicleId == x.Id).Select(c => new { c.Category, c.Total, c.Liters, c.Kwh }),
                YearTotal = costs.Where(c => c.VehicleId == x.Id).Sum(c => c.Total)
            }));
        });

        v.MapPost("", async (Guid orgId, VehicleInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var plate = FormatPlate(input.Plate);
            if (await db.Vehicles.AnyAsync(x => x.OrganizationId == orgId && x.Plate == plate, ct)) throw new AppException(409, "Matrícula já registada");
            var vehicle = new Vehicle { OrganizationId = orgId };
            Apply(vehicle, input with { Plate = plate });
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync(ct);
            return Results.Ok(vehicle);
        });

        v.MapPut("{id:guid}", async (Guid orgId, Guid id, VehicleInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct) ?? throw new AppException(404, "Viatura não encontrada");
            Apply(vehicle, input with { Plate = FormatPlate(input.Plate) });
            await db.SaveChangesAsync(ct);
            return Results.Ok(vehicle);
        });

        v.MapDelete("{id:guid}", async (Guid orgId, Guid id, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireManageAsync(orgId, ct);
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct) ?? throw new AppException(404, "Viatura não encontrada");
            if (await db.Invoices.AnyAsync(i => i.VehicleId == id, ct)) vehicle.IsActive = false; // keep history
            else db.Vehicles.Remove(vehicle);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ── Categories (global defaults + per-organization) ──
        var c = app.MapGroup("/api/orgs/{orgId:guid}/categories").RequireAuthorization().WithTags("Categories");

        c.MapGet("", async (Guid orgId, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            await access.GetAsync(orgId, ct);
            return Results.Ok(await db.Categories.Where(x => x.OrganizationId == null || x.OrganizationId == orgId)
                .OrderBy(x => x.Name).Select(x => new
                {
                    x.Id, x.Name, x.Color, x.Keywords, x.IsVehicleRelated, x.VatDeductiblePercent, x.IsActive,
                    IsGlobal = x.OrganizationId == null
                }).ToListAsync(ct));
        });

        c.MapPost("", async (Guid orgId, CategoryInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            if (!a.CanManage && !a.IsAccountant) throw new AppException(403, "Sem permissão");
            var cat = new Category { OrganizationId = orgId };
            Apply(cat, input);
            db.Categories.Add(cat);
            await db.SaveChangesAsync(ct);
            return Results.Ok(cat);
        });

        c.MapPut("{id:guid}", async (Guid orgId, Guid id, CategoryInput input, AccessService access, AppDbContext db, CancellationToken ct) =>
        {
            var a = await access.GetAsync(orgId, ct);
            var cat = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException(404, "Categoria não encontrada");
            var allowed = cat.OrganizationId == null ? a.IsAdmin : cat.OrganizationId == orgId && (a.CanManage || a.IsAccountant);
            if (!allowed) throw new AppException(403, "As categorias predefinidas só podem ser alteradas no backoffice");
            Apply(cat, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(cat);
        });
    }

    private static string FormatPlate(string plate)
    {
        var p = Text.NormalizePlate(plate);
        return p.Length == 6 ? $"{p[..2]}-{p[2..4]}-{p[4..]}" : plate.Trim().ToUpperInvariant();
    }

    private static void Apply(Vehicle v, VehicleInput i)
    {
        v.Plate = i.Plate; v.Make = i.Make; v.Model = i.Model; v.FuelType = i.FuelType; v.AcquiredAt = i.AcquiredAt;
        v.AcquisitionValue = i.AcquisitionValue; v.InsuranceRenewal = i.InsuranceRenewal; v.InspectionDue = i.InspectionDue;
        v.Notes = i.Notes; v.IsActive = i.IsActive;
    }

    private static void Apply(Category c, CategoryInput i)
    {
        c.Name = i.Name.Trim(); c.Color = i.Color ?? c.Color; c.Keywords = i.Keywords ?? ""; c.IsVehicleRelated = i.IsVehicleRelated;
        c.VatDeductiblePercent = i.VatDeductiblePercent; c.IsActive = i.IsActive;
    }
}
