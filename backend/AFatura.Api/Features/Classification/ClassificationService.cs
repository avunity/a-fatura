using AFatura.Api.Common;
using AFatura.Api.Data;
using AFatura.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AFatura.Api.Features.Classification;

public record CategoryGuess(Guid? CategoryId, decimal Confidence, string Reason);
public record VehicleGuess(Guid? VehicleId, string Reason);

/// <summary>
/// Category: learned supplier default → AI suggestion → keyword rules.
/// Vehicle: plate printed on document → learned supplier default → the only vehicle that fits.
/// </summary>
public class ClassificationService(AppDbContext db)
{
    public Task<List<Category>> CategoriesForAsync(Guid orgId, CancellationToken ct = default) =>
        db.Categories.AsNoTracking()
            .Where(c => c.IsActive && (c.OrganizationId == null || c.OrganizationId == orgId))
            .OrderBy(c => c.Name).ToListAsync(ct);

    public CategoryGuess GuessCategory(IReadOnlyList<Category> categories, Supplier? supplier,
        string? aiCategory, decimal? aiConfidence, params string?[] texts)
    {
        if (supplier?.DefaultCategoryId is { } learned && categories.Any(c => c.Id == learned))
            return new(learned, 0.95m, "Fornecedor já classificado anteriormente");

        if (!string.IsNullOrWhiteSpace(aiCategory))
        {
            var folded = Text.Fold(aiCategory);
            var match = categories.FirstOrDefault(c => Text.Fold(c.Name) == folded)
                        ?? categories.FirstOrDefault(c => Text.Fold(c.Name).Contains(folded) || folded.Contains(Text.Fold(c.Name)));
            if (match != null) return new(match.Id, Math.Clamp(aiConfidence ?? 0.7m, 0, 1), "Sugerido pela IA");
        }

        var haystack = " " + Text.Fold(string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t)))) + " ";
        if (haystack.Trim().Length > 0)
        {
            var best = categories
                .Select(c => new
                {
                    c,
                    hits = c.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(Text.Fold).Where(k => k.Length > 1)
                        .Count(k => haystack.Contains(" " + k) || haystack.Contains(k + " "))
                })
                .Where(x => x.hits > 0)
                .OrderByDescending(x => x.hits).FirstOrDefault();
            if (best != null) return new(best.c.Id, Math.Min(0.5m + best.hits * 0.1m, 0.8m), "Palavras-chave");
        }

        return new(null, 0, "Sem classificação");
    }

    public async Task<VehicleGuess> GuessVehicleAsync(Guid orgId, string? detectedPlate, Supplier? supplier,
        Category? category, CancellationToken ct = default)
    {
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.OrganizationId == orgId && v.IsActive).ToListAsync(ct);
        if (vehicles.Count == 0) return new(null, "");

        var plate = Text.NormalizePlate(detectedPlate);
        if (plate.Length >= 6)
        {
            var byPlate = vehicles.FirstOrDefault(v => Text.NormalizePlate(v.Plate) == plate);
            if (byPlate != null) return new(byPlate.Id, $"Matrícula {byPlate.Plate} encontrada no documento");
        }

        if (category is not { IsVehicleRelated: true }) return new(null, "");

        if (supplier?.DefaultVehicleId is { } learned && vehicles.Any(v => v.Id == learned))
            return new(learned, "Viatura habitual deste fornecedor");

        var candidates = Text.Fold(category.Name).Contains("eletric") || Text.Fold(category.Name).Contains("carregamento")
            ? vehicles.Where(v => v.FuelType is FuelType.Electric or FuelType.PlugInHybrid).ToList()
            : Text.Fold(category.Name).Contains("combustivel")
                ? vehicles.Where(v => v.FuelType != FuelType.Electric).ToList()
                : vehicles;

        return candidates.Count == 1 ? new(candidates[0].Id, "Única viatura compatível") : new(null, "");
    }

    /// <summary>Called when a user corrects category/vehicle: remember it for the next documents of this supplier.</summary>
    public async Task LearnAsync(Guid orgId, string? supplierNif, Guid? categoryId, Guid? vehicleId, CancellationToken ct = default)
    {
        var nif = Text.NormalizeNif(supplierNif);
        if (nif.Length == 0) return;
        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.OrganizationId == orgId && x.Nif == nif, ct);
        if (s == null) { s = new Supplier { OrganizationId = orgId, Nif = nif }; db.Suppliers.Add(s); }
        if (categoryId != null) s.DefaultCategoryId = categoryId;
        if (vehicleId != null) s.DefaultVehicleId = vehicleId;
    }
}
