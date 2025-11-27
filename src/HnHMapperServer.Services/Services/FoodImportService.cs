using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HnHMapperServer.Services.Services;

public class FoodImportService : IFoodImportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<FoodImportService> _logger;
    private const int BatchSize = 1000;

    // Mapping from external FEP names to our attribute names
    private static readonly Dictionary<string, string> FepAttributeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Strength", "STR" },
        { "Agility", "AGI" },
        { "Intelligence", "INT" },
        { "Constitution", "CON" },
        { "Perception", "PER" },
        { "Charisma", "CHA" },
        { "Dexterity", "DEX" },
        { "Will", "WILL" },
        { "Psyche", "PSY" }
    };

    public FoodImportService(ApplicationDbContext dbContext, ILogger<FoodImportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<FoodImportResult> ImportFromJsonFileAsync(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
        }

        var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
        return await ImportFromJsonContentAsync(jsonContent);
    }

    public async Task<FoodImportResult> ImportFromJsonContentAsync(string jsonContent)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new FoodImportResult
        {
            CompletedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting food import from JSON");

            // Get the first admin user to use as the submitter
            // Use IgnoreQueryFilters since we're importing global data
            var adminUser = await _dbContext.Users
                .IgnoreQueryFilters()
                .Where(u => u.UserName == "admin")
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

            if (adminUser == null)
            {
                result.Errors.Add("No admin user found. Please ensure an admin user exists before importing.");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            // Parse JSON
            var jsonItems = JsonSerializer.Deserialize<List<JsonFoodItem>>(jsonContent);
            if (jsonItems == null || jsonItems.Count == 0)
            {
                result.Errors.Add("Failed to parse JSON or no items found");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            result.TotalItems = jsonItems.Count;
            _logger.LogInformation("Parsed {Count} food items from JSON", result.TotalItems);

            // Process in batches
            var batches = jsonItems
                .Select((item, index) => new { Item = item, Index = index })
                .GroupBy(x => x.Index / BatchSize)
                .Select(g => g.Select(x => x.Item).ToList())
                .ToList();

            _logger.LogInformation("Processing {BatchCount} batches of {BatchSize} items", batches.Count, BatchSize);

            foreach (var batch in batches)
            {
                var foodEntities = new List<FoodEntity>();

                foreach (var jsonItem in batch)
                {
                    try
                    {
                        var foodEntity = MapJsonItemToFoodEntity(jsonItem, adminUser);
                        foodEntities.Add(foodEntity);
                        result.ItemsImported++;
                    }
                    catch (Exception ex)
                    {
                        result.ItemsSkipped++;
                        result.Errors.Add($"Error mapping item '{jsonItem.ItemName}': {ex.Message}");
                        _logger.LogWarning(ex, "Failed to map food item: {ItemName}", jsonItem.ItemName);
                    }
                }

                // Bulk insert batch
                if (foodEntities.Count > 0)
                {
                    await _dbContext.Foods.AddRangeAsync(foodEntities);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Imported batch: {Count} items (Total: {Total}/{TotalItems})",
                        foodEntities.Count, result.ItemsImported, result.TotalItems);
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            _logger.LogInformation(
                "Food import completed: {Imported} imported, {Skipped} skipped, {Errors} errors in {Duration}",
                result.ItemsImported, result.ItemsSkipped, result.Errors.Count, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during food import");
            result.Errors.Add($"Fatal error: {ex.Message}");
            result.Duration = stopwatch.Elapsed;
            return result;
        }
    }

    private FoodEntity MapJsonItemToFoodEntity(JsonFoodItem jsonItem, string adminUserId)
    {
        var foodEntity = new FoodEntity
        {
            Name = jsonItem.ItemName,
            Hunger = (int)jsonItem.Hunger,  // Convert decimal to int
            Energy = (int)jsonItem.Energy,  // Convert decimal to int
            TenantId = null, // Global recipe (null TenantId means global)
            IsVerified = true,
            IsSmoked = jsonItem.ItemName.Contains("Smoked", StringComparison.OrdinalIgnoreCase),
            ResourceType = ExtractResourceType(jsonItem.ResourceName),
            Description = null,
            SubmittedBy = adminUserId, // Use admin user ID
            CreatedAt = DateTime.UtcNow,
            ApprovedBy = adminUserId, // Use admin user ID
            ApprovedAt = DateTime.UtcNow
        };

        // Map ingredients - don't set Food reference, let EF Core handle it via collection
        foreach (var jsonIngredient in jsonItem.Ingredients)
        {
            foodEntity.Ingredients.Add(new FoodIngredientEntity
            {
                Name = jsonIngredient.Name,
                Quantity = jsonIngredient.Percentage / 100m, // Convert percentage to decimal
                Quality = null // Not provided in external JSON
                // Don't set FoodId or Food - EF Core will handle via navigation property
            });
        }

        // Map FEPs - don't set Food reference, let EF Core handle it via collection
        foreach (var jsonFep in jsonItem.Feps)
        {
            var attributeName = ParseFepAttributeName(jsonFep.Name);
            if (!string.IsNullOrEmpty(attributeName))
            {
                foodEntity.Feps.Add(new FoodFepEntity
                {
                    AttributeName = attributeName,
                    BaseValue = jsonFep.Value
                    // Don't set FoodId or Food - EF Core will handle via navigation property
                });
            }
        }

        return foodEntity;
    }

    /// <summary>
    /// Extract resource type from path like "gfx/invobjs/autumnsteak" -> "autumnsteak"
    /// </summary>
    private string ExtractResourceType(string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName))
            return string.Empty;

        var parts = resourceName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : resourceName;
    }

    /// <summary>
    /// Parse FEP name like "Perception +1" or "Strength +2" to our attribute format
    /// </summary>
    private string ParseFepAttributeName(string fepName)
    {
        // Pattern: "AttributeName +Tier"
        var match = Regex.Match(fepName, @"^(\w+)\s*\+(\d+)$");
        if (!match.Success)
        {
            _logger.LogWarning("Failed to parse FEP name: {FepName}", fepName);
            return string.Empty;
        }

        var attributeName = match.Groups[1].Value;
        var tier = match.Groups[2].Value;

        // Map to our attribute abbreviation
        if (FepAttributeMap.TryGetValue(attributeName, out var abbreviation))
        {
            // Include tier in the attribute name (e.g., "PER +1", "STR +2")
            return $"{abbreviation} +{tier}";
        }

        _logger.LogWarning("Unknown FEP attribute: {AttributeName}", attributeName);
        return string.Empty;
    }
}
