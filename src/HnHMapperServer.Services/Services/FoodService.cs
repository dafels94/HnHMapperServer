using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for managing food/recipe data in the cookbook
/// </summary>
public class FoodService : IFoodService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IVerifiedContributorService _verifiedContributorService;
    private readonly FoodMetricsCalculator _metricsCalculator;
    private readonly IMemoryCache _cache;

    // Static semaphore to prevent cache stampede (concurrent loads of same data)
    private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    public FoodService(
        ApplicationDbContext context,
        ITenantContextAccessor tenantContext,
        IVerifiedContributorService verifiedContributorService,
        FoodMetricsCalculator metricsCalculator,
        IMemoryCache cache)
    {
        _context = context;
        _tenantContext = tenantContext;
        _verifiedContributorService = verifiedContributorService;
        _metricsCalculator = metricsCalculator;
        _cache = cache;
    }

    public async Task<List<FoodDto>> GetAllFoodsAsync(CancellationToken cancellationToken = default)
    {
        // Cache key includes tenant ID to ensure tenant isolation
        var tenantId = _tenantContext.GetCurrentTenantId() ?? "global";
        var cacheKey = $"AllFoods_{tenantId}";

        // Try to get from cache first (fast path - no lock needed)
        if (_cache.TryGetValue<List<FoodDto>>(cacheKey, out var cachedFoods) && cachedFoods != null)
        {
            return cachedFoods;
        }

        // Cache miss - acquire lock to prevent stampede (only 1 request loads data, others wait)
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check cache after acquiring lock (another request may have loaded it)
            if (_cache.TryGetValue<List<FoodDto>>(cacheKey, out cachedFoods) && cachedFoods != null)
            {
                return cachedFoods;
            }

            // Still not in cache - load from database
            // Use AsSplitQuery to load collections in separate queries (faster for large datasets)
            var foods = await _context.Foods
                .Where(f => f.Hunger > 0)  // Only include foods with hunger data
                .Include(f => f.Ingredients)
                .Include(f => f.Feps)
                .AsSplitQuery()
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync(cancellationToken);

            var foodDtos = foods.Select(MapToDto).ToList();

            // Calculate metrics for all foods
            foreach (var food in foodDtos)
            {
                _metricsCalculator.CalculateFoodMetrics(food);
            }

            // Assign dynamic tiers
            _metricsCalculator.AssignTiers(foodDtos);

            // Cache for 5 minutes (balance between performance and data freshness)
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                .SetSize(1); // Each cache entry counts as 1 unit

            _cache.Set(cacheKey, foodDtos, cacheOptions);

            return foodDtos;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<(List<FoodDto> Foods, int TotalCount)> SearchFoodsAsync(FoodSearchDto search, CancellationToken cancellationToken = default)
    {
        var query = _context.Foods
            .Where(f => f.Hunger > 0)  // Only include foods with hunger data (efficiency requires Hunger > 0)
            .Include(f => f.Ingredients)
            .Include(f => f.Feps)
            .AsSplitQuery()
            .AsQueryable();

        // Text search (name or resource type) - use EF.Functions.Like for SQLite compatibility
        if (!string.IsNullOrWhiteSpace(search.SearchTerm))
        {
            var pattern = $"%{search.SearchTerm}%";
            query = query.Where(f =>
                EF.Functions.Like(f.Name, pattern) ||
                (f.ResourceType != null && EF.Functions.Like(f.ResourceType, pattern)) ||
                (f.Description != null && EF.Functions.Like(f.Description, pattern))
            );
        }

        // Filter by ingredient - use EF.Functions.Like for SQLite compatibility
        if (!string.IsNullOrWhiteSpace(search.Ingredient))
        {
            var pattern = $"%{search.Ingredient}%";
            query = query.Where(f =>
                f.Ingredients.Any(i => EF.Functions.Like(i.Name, pattern))
            );
        }

        // Filter by FEP type
        if (!string.IsNullOrWhiteSpace(search.FepType))
        {
            var fepType = search.FepType.ToUpper();
            query = query.Where(f =>
                f.Feps.Any(fep => fep.AttributeName.ToUpper() == fepType && fep.BaseValue > 0)
            );
        }

        // Filter by max hunger
        if (search.MaxHunger.HasValue)
        {
            query = query.Where(f => f.Hunger <= search.MaxHunger.Value);
        }

        // Filter by tenant scope
        var tenantId = _tenantContext.GetCurrentTenantId();
        if (!search.IncludeGlobal && !search.IncludeTenant)
        {
            // If both disabled, show nothing
            return (new List<FoodDto>(), 0);
        }
        else if (!search.IncludeGlobal)
        {
            // Show only tenant-specific
            query = query.Where(f => f.TenantId == tenantId);
        }
        else if (!search.IncludeTenant)
        {
            // Show only global
            query = query.Where(f => f.TenantId == null);
        }
        // else: show both (default query filter handles this)

        // CRITICAL FIX: Load ALL filtered foods (not paginated) to calculate correct tier percentiles
        var allFoods = await query
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

        // Convert ALL to DTOs
        var allFoodDtos = allFoods.Select(MapToDto).ToList();

        // Calculate metrics for ALL foods
        foreach (var food in allFoodDtos)
        {
            _metricsCalculator.CalculateFoodMetrics(food);
        }

        // Assign dynamic tiers based on FULL dataset percentiles (this is the correct behavior!)
        _metricsCalculator.AssignTiers(allFoodDtos);

        // Apply advanced filters (post-calculation, pre-pagination)
        var filteredFoods = allFoodDtos.AsEnumerable();

        if (search.MinPurity.HasValue)
        {
            filteredFoods = filteredFoods.Where(f => f.Purity >= search.MinPurity.Value);
        }

        if (!string.IsNullOrWhiteSpace(search.Tier))
        {
            filteredFoods = filteredFoods.Where(f => f.Tier.Equals(search.Tier, StringComparison.OrdinalIgnoreCase));
        }

        if (search.MinEfficiency.HasValue)
        {
            filteredFoods = filteredFoods.Where(f => f.Efficiency >= search.MinEfficiency.Value);
        }

        // Apply custom sorting
        filteredFoods = ApplySorting(filteredFoods, search.SortBy, search.SortDescending);

        // Get total count AFTER filtering (for correct pagination)
        var totalCount = filteredFoods.Count();

        // NOW apply pagination (at the very end)
        var result = filteredFoods
            .Skip(search.Skip)
            .Take(search.Take)
            .ToList();

        return (result, totalCount);
    }

    private IEnumerable<FoodDto> ApplySorting(IEnumerable<FoodDto> foods, string? sortBy, bool descending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by efficiency descending
            return descending ? foods.OrderByDescending(f => f.Efficiency) : foods.OrderBy(f => f.Efficiency);
        }

        var sorted = sortBy.ToLower() switch
        {
            "efficiency" => descending ? foods.OrderByDescending(f => f.Efficiency) : foods.OrderBy(f => f.Efficiency),
            "purity" => descending ? foods.OrderByDescending(f => f.Purity) : foods.OrderBy(f => f.Purity),
            "name" => descending ? foods.OrderByDescending(f => f.Name) : foods.OrderBy(f => f.Name),
            "hunger" => descending ? foods.OrderByDescending(f => f.Hunger) : foods.OrderBy(f => f.Hunger),
            "powerscore" => descending ? foods.OrderByDescending(f => f.PowerScore) : foods.OrderBy(f => f.PowerScore),
            "fighterscore" => descending ? foods.OrderByDescending(f => f.FighterScore) : foods.OrderBy(f => f.FighterScore),
            "crafterscore" => descending ? foods.OrderByDescending(f => f.CrafterScore) : foods.OrderBy(f => f.CrafterScore),
            "tier" => descending ? foods.OrderByDescending(f => f.TierValue) : foods.OrderBy(f => f.TierValue),
            _ => descending ? foods.OrderByDescending(f => f.Efficiency) : foods.OrderBy(f => f.Efficiency)
        };

        return sorted;
    }

    public async Task<FoodDto?> GetFoodByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var food = await _context.Foods
            .Include(f => f.Ingredients)
            .Include(f => f.Feps)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (food == null) return null;

        var foodDto = MapToDto(food);

        // Calculate metrics for this food
        _metricsCalculator.CalculateFoodMetrics(foodDto);

        // NOTE: We don't assign tier for single food view because tier is relative to the full dataset.
        // Calling AssignTiers on a 1-food list would always show "Best" which is misleading.
        // The tier will be shown as "Unknown" or can be calculated on-demand if needed.
        // Alternatively, this could call SearchFoodsAsync to get the food in context of all foods.
        foodDto.Tier = "Unknown";
        foodDto.TierValue = 0.0m;

        return foodDto;
    }

    public async Task<FoodDto> CreateFoodAsync(SubmitFoodDto foodDto, string userId, CancellationToken cancellationToken = default)
    {
        var tenantId = foodDto.IsGlobal ? null : _tenantContext.GetCurrentTenantId();

        var food = new FoodEntity
        {
            TenantId = tenantId,
            Name = foodDto.Name,
            ResourceType = foodDto.ResourceType,
            Energy = foodDto.Energy,
            Hunger = foodDto.Hunger,
            Description = foodDto.Description,
            IsSmoked = foodDto.IsSmoked,
            SubmittedBy = userId,
            CreatedAt = DateTime.UtcNow,
            IsVerified = true,
            ApprovedBy = userId,
            ApprovedAt = DateTime.UtcNow
        };

        // Add ingredients
        foreach (var ingredientDto in foodDto.Ingredients)
        {
            food.Ingredients.Add(new FoodIngredientEntity
            {
                Name = ingredientDto.Name,
                Quantity = ingredientDto.Quantity,
                Quality = ingredientDto.Quality
            });
        }

        // Add FEPs
        foreach (var fepDto in foodDto.Feps)
        {
            food.Feps.Add(new FoodFepEntity
            {
                AttributeName = fepDto.AttributeName,
                BaseValue = fepDto.BaseValue
            });
        }

        _context.Foods.Add(food);
        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cache after creating a food
        var cacheKey = $"AllFoods_{tenantId ?? "global"}";
        _cache.Remove(cacheKey);

        // Reload with navigation properties
        var created = await _context.Foods
            .Include(f => f.Ingredients)
            .Include(f => f.Feps)
            .FirstAsync(f => f.Id == food.Id, cancellationToken);

        var createdDto = MapToDto(created);

        // Calculate metrics for newly created food
        _metricsCalculator.CalculateFoodMetrics(createdDto);

        // Don't assign tier for single food (see GetFoodByIdAsync comment)
        createdDto.Tier = "Unknown";
        createdDto.TierValue = 0.0m;

        return createdDto;
    }

    public async Task<FoodDto?> UpdateFoodAsync(int id, SubmitFoodDto foodDto, string userId, CancellationToken cancellationToken = default)
    {
        var food = await _context.Foods
            .Include(f => f.Ingredients)
            .Include(f => f.Feps)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (food == null)
        {
            return null;
        }

        // Update basic properties
        food.Name = foodDto.Name;
        food.ResourceType = foodDto.ResourceType;
        food.Energy = foodDto.Energy;
        food.Hunger = foodDto.Hunger;
        food.Description = foodDto.Description;
        food.IsSmoked = foodDto.IsSmoked;

        // Remove old ingredients and FEPs
        _context.FoodIngredients.RemoveRange(food.Ingredients);
        _context.FoodFeps.RemoveRange(food.Feps);

        food.Ingredients.Clear();
        food.Feps.Clear();

        // Add new ingredients
        foreach (var ingredientDto in foodDto.Ingredients)
        {
            food.Ingredients.Add(new FoodIngredientEntity
            {
                Name = ingredientDto.Name,
                Quantity = ingredientDto.Quantity,
                Quality = ingredientDto.Quality
            });
        }

        // Add new FEPs
        foreach (var fepDto in foodDto.Feps)
        {
            food.Feps.Add(new FoodFepEntity
            {
                AttributeName = fepDto.AttributeName,
                BaseValue = fepDto.BaseValue
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cache after updating a food
        var tenantId = _tenantContext.GetCurrentTenantId() ?? "global";
        var cacheKey = $"AllFoods_{tenantId}";
        _cache.Remove(cacheKey);

        // Reload with navigation properties
        var updated = await _context.Foods
            .Include(f => f.Ingredients)
            .Include(f => f.Feps)
            .FirstAsync(f => f.Id == food.Id, cancellationToken);

        var updatedDto = MapToDto(updated);

        // Calculate metrics for updated food
        _metricsCalculator.CalculateFoodMetrics(updatedDto);

        // Don't assign tier for single food (see GetFoodByIdAsync comment)
        updatedDto.Tier = "Unknown";
        updatedDto.TierValue = 0.0m;

        return updatedDto;
    }

    public async Task<bool> DeleteFoodAsync(int id, CancellationToken cancellationToken = default)
    {
        var food = await _context.Foods
            .Include(f => f.Feps)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (food == null)
        {
            return false;
        }

        var tenantId = food.TenantId ?? "global";

        _context.Foods.Remove(food);
        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cache after deleting a food
        var cacheKey = $"AllFoods_{tenantId}";
        _cache.Remove(cacheKey);

        return true;
    }

    public async Task<bool> IsVerifiedContributorAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _verifiedContributorService.IsVerifiedContributorAsync(userId, cancellationToken);
    }

    public async Task<List<FoodDto>> GetTopFoodsByStatAsync(string stat, int limit = 3, CancellationToken cancellationToken = default)
    {
        // Get all foods with metrics calculated
        var allFoods = await GetAllFoodsAsync(cancellationToken);

        // Filter foods that have the specified stat
        var foodsWithStat = allFoods.Where(f =>
            f.Feps.Any(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // Calculate stat value for each food (tier-weighted)
        foreach (var food in foodsWithStat)
        {
            food.StatValue = food.Feps
                .Where(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
                .Sum(fep =>
                {
                    // Extract tier from FEP name (e.g., "STR +2" → 2)
                    var tierMatch = Regex.Match(fep.AttributeName, @"\+(\d+)");
                    var tier = tierMatch.Success ? int.Parse(tierMatch.Groups[1].Value) : 1;

                    // Apply tier multiplier (1.5× for tier +2)
                    return fep.BaseValue * (tier == 2 ? 1.5m : 1.0m);
                });
        }

        // Sort by stat value (desc), then efficiency (desc)
        return foodsWithStat
            .OrderByDescending(f => f.StatValue)
            .ThenByDescending(f => f.Efficiency)
            .Take(limit)
            .ToList();
    }

    public async Task<List<GroupedFoodDto>> GetTopFoodsByRoleAsync(string role, int limit = 20, CancellationToken cancellationToken = default)
    {
        // Get all foods with metrics calculated
        var allFoods = await GetAllFoodsAsync(cancellationToken);

        // Filter by role category AND exclude zero hunger foods (invalid data)
        var filtered = role.ToLower() switch
        {
            "fighter" => allFoods.Where(f => f.FighterPercent > 60 && f.Hunger > 0),
            "crafter" => allFoods.Where(f => f.CrafterPercent > 60 && f.Hunger > 0),
            "universal" => allFoods.Where(f =>
                f.FighterPercent >= 40 && f.FighterPercent <= 60 &&
                f.CrafterPercent >= 40 && f.CrafterPercent <= 60 &&
                f.Hunger > 0
            ),
            _ => Enumerable.Empty<FoodDto>()
        };

        // Sort by efficiency (desc), then tier value (desc)
        var sorted = filtered
            .OrderByDescending(f => f.Efficiency)
            .ThenByDescending(f => f.TierValue)
            .Take(limit)
            .ToList();

        // Group by food name
        return GroupFoodsByName(sorted);
    }

    public async Task<StatAnalysisDto> GetStatAnalysisAsync(string stat, CancellationToken cancellationToken = default)
    {
        // Load ALL foods from cache (or DB if cache miss)
        // This reuses the existing AllFoods_{tenantId} cache
        var allFoods = await GetAllFoodsAsync(cancellationToken);

        // Filter in-memory for foods that have the specified stat
        // Much faster than SQL query since data is already loaded
        var foodsWithStat = allFoods.Where(f =>
            f.Feps.Any(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        if (foodsWithStat.Count == 0)
        {
            return new StatAnalysisDto { Stat = stat, TotalFoods = 0 };
        }

        // Calculate stat-specific metrics for filtered foods
        foreach (var food in foodsWithStat)
        {
            var totalFep = food.Feps.Sum(f => (double)f.BaseValue);
            if (totalFep == 0) continue;

            // StatValue = expected stat per eating (probability × tier multiplier)
            food.StatValue = (decimal)food.Feps
                .Where(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
                .Sum(fep =>
                {
                    var tierMult = fep.AttributeName.Contains("+2") ? 2 : 1;
                    return ((double)fep.BaseValue / totalFep) * tierMult;
                });

            // StatConcentration = % of food that is target stat (0.0 to 1.0)
            var statFep = food.Feps
                .Where(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
                .Sum(f => (double)f.BaseValue);
            food.StatConcentration = (decimal)(statFep / totalFep);

            // HasStatTier2 = has +2 tier for this specific stat
            food.HasStatTier2 = food.Feps.Any(fep =>
                fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase) &&
                fep.AttributeName.Contains("+2"));

            // StatPerEating = total expected stat per eating (not normalized by totalFep)
            var statPerEating = food.Feps
                .Where(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
                .Sum(fep =>
                {
                    var tierMult = fep.AttributeName.Contains("+2") ? 2 : 1;
                    return (double)fep.BaseValue * tierMult;
                });

            // StatEfficiency = expected stat per hunger (higher = better)
            food.StatEfficiency = food.Hunger > 0
                ? (decimal)(statPerEating / food.Hunger)
                : 0;
        }

        // Group by tier (tiers already assigned by GetAllFoodsAsync)
        var bestTier = foodsWithStat.Where(f => f.Tier == "Best")
            .OrderByDescending(f => f.StatValue)
            .ThenByDescending(f => f.Efficiency)
            .ToList();
        var midTier = foodsWithStat.Where(f => f.Tier == "Mid")
            .OrderByDescending(f => f.StatValue)
            .ThenByDescending(f => f.Efficiency)
            .ToList();
        var lowTier = foodsWithStat.Where(f => f.Tier == "Low")
            .OrderByDescending(f => f.StatValue)
            .ThenByDescending(f => f.Efficiency)
            .ToList();

        // Get top 5 overall (regardless of tier)
        var topOverall = foodsWithStat
            .OrderByDescending(f => f.StatValue)
            .ThenByDescending(f => f.Efficiency)
            .Take(5)
            .ToList();

        // Group foods by name for each tier
        var bestTierGrouped = GroupFoodsByName(bestTier);
        var midTierGrouped = GroupFoodsByName(midTier);
        var lowTierGrouped = GroupFoodsByName(lowTier);
        var topOverallGrouped = GroupFoodsByName(topOverall);

        return new StatAnalysisDto
        {
            Stat = stat,
            TotalFoods = foodsWithStat.Count,
            BestTier = bestTierGrouped,
            MidTier = midTierGrouped,
            LowTier = lowTierGrouped,
            TopOverall = topOverallGrouped
        };
    }

    public async Task<FeastPlannerResponseDto> GetFeastPlannerAsync(string stat, int highestStat, int limit = 20, CancellationToken cancellationToken = default)
    {
        // Load all foods from cache
        var allFoods = await GetAllFoodsAsync(cancellationToken);

        // Filter foods that have the specified stat and have hunger > 0
        var foodsWithStat = allFoods.Where(f =>
            f.Hunger > 0 &&
            f.Feps.Any(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // Calculate stat efficiency for each food
        var results = new List<FeastPlannerResultDto>();

        foreach (var food in foodsWithStat)
        {
            // Calculate StatPerEating = Σ(stat_fep × tier_mult)
            var statPerEating = food.Feps
                .Where(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
                .Sum(fep =>
                {
                    var tierMult = fep.AttributeName.Contains("+2") ? 2 : 1;
                    return (double)fep.BaseValue * tierMult;
                });

            if (statPerEating <= 0) continue;

            // StatEfficiency = stat per hunger
            var statEfficiency = statPerEating / food.Hunger;

            // HungerPerStat = highestStat / StatEfficiency = hunger needed for +1 stat
            var hungerPerStat = highestStat / statEfficiency;

            // Update food's StatEfficiency for display
            food.StatEfficiency = (decimal)statEfficiency;

            // Also set other stat-specific properties
            var totalFep = food.Feps.Sum(f => (double)f.BaseValue);
            var statFep = food.Feps
                .Where(fep => fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase))
                .Sum(f => (double)f.BaseValue);
            food.StatConcentration = totalFep > 0 ? (decimal)(statFep / totalFep) : 0;
            food.HasStatTier2 = food.Feps.Any(fep =>
                fep.AttributeName.StartsWith(stat, StringComparison.OrdinalIgnoreCase) &&
                fep.AttributeName.Contains("+2"));

            results.Add(new FeastPlannerResultDto
            {
                Food = food,
                HungerPerStat = (decimal)hungerPerStat
            });
        }

        // Sort by HungerPerStat ascending (lower = better)
        var sortedResults = results
            .OrderBy(r => r.HungerPerStat)
            .Take(limit)
            .ToList();

        return new FeastPlannerResponseDto
        {
            Stat = stat,
            HighestStat = highestStat,
            Results = sortedResults
        };
    }

    private static FoodDto MapToDto(FoodEntity entity)
    {
        return new FoodDto
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Name = entity.Name,
            ResourceType = entity.ResourceType,
            Energy = entity.Energy,
            Hunger = entity.Hunger,
            Description = entity.Description,
            IsSmoked = entity.IsSmoked,
            SubmittedBy = entity.SubmittedBy,
            CreatedAt = entity.CreatedAt,
            IsVerified = entity.IsVerified,
            Ingredients = entity.Ingredients.Select(i => new FoodIngredientDto
            {
                Id = i.Id,
                Name = i.Name,
                Quantity = i.Quantity,
                Quality = i.Quality
            }).ToList(),
            Feps = entity.Feps.Select(f => new FoodFepDto
            {
                Id = f.Id,
                AttributeName = f.AttributeName,
                BaseValue = f.BaseValue
            }).ToList()
        };
    }

    /// <summary>
    /// Groups foods by name (including smoked suffix) and selects best variant by efficiency
    /// </summary>
    /// <param name="foods">List of foods to group</param>
    /// <returns>List of grouped foods with best variant selected</returns>
    public List<GroupedFoodDto> GroupFoodsByName(List<FoodDto> foods)
    {
        return foods
            .GroupBy(f => $"{f.Name}{(f.IsSmoked ? " (Smoked)" : "")}")
            .Select(group =>
            {
                // Calculate efficiency for each food to find the best variant
                var variantsWithEfficiency = group.Select(food => new
                {
                    Food = food,
                    Efficiency = CalculateTierAdjustedEfficiency(food)
                }).ToList();

                // Select best variant by efficiency
                var bestVariant = variantsWithEfficiency
                    .OrderByDescending(v => v.Efficiency)
                    .First().Food;

                return new GroupedFoodDto
                {
                    FoodName = group.Key,
                    IsSmoked = group.First().IsSmoked,
                    VariantCount = group.Count(),
                    BestVariant = bestVariant,
                    AllVariants = group.OrderByDescending(f => CalculateTierAdjustedEfficiency(f)).ToList()
                };
            })
            .ToList();
    }

    /// <summary>
    /// Calculates tier-adjusted efficiency with concentration bonus
    /// </summary>
    private decimal CalculateTierAdjustedEfficiency(FoodDto food)
    {
        if (!food.Feps.Any() || food.Hunger == 0)
            return 0;

        var totalFep = food.Feps.Sum(f => (double)f.BaseValue);
        if (totalFep == 0)
            return 0;

        // Extract base stat name
        string GetBaseStat(string attributeName) => attributeName.Split('+')[0].Trim();

        // Get tier multiplier
        int GetTierMultiplier(string attributeName) => attributeName.Contains("+2") ? 2 : 1;

        // Group by base stat to find dominant stat percentage
        var groupedByBaseStat = food.Feps
            .GroupBy(f => GetBaseStat(f.AttributeName))
            .Select(g => new { BaseStat = g.Key, TotalFep = g.Sum(f => (double)f.BaseValue) })
            .OrderByDescending(g => g.TotalFep)
            .ToList();

        var dominantStatPercent = (groupedByBaseStat.First().TotalFep / totalFep) * 100;

        // Calculate tier-adjusted expected stat value
        double expectedStatValue = 0;
        foreach (var fep in food.Feps)
        {
            var probability = (double)fep.BaseValue / totalFep;
            var tierMultiplier = GetTierMultiplier(fep.AttributeName);
            expectedStatValue += probability * tierMultiplier;
        }

        // Apply concentration bonus
        var baseEfficiency = expectedStatValue / food.Hunger;
        var concentrationBonus = 1 + (dominantStatPercent / 100);

        return (decimal)(baseEfficiency * concentrationBonus);
    }
}
