using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Services.Interfaces;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for calculating Food Event Points (FEPs) with quality multipliers
/// Implements the HnH formula: effectiveFep = baseFep * sqrt(quality / 10)
/// </summary>
public class FepCalculatorService : IFepCalculatorService
{
    /// <summary>
    /// Calculate effective FEP value based on quality
    /// Formula: effectiveFep = baseFep * sqrt(quality / 10)
    /// Examples:
    /// - Q10 food: 1.0x multiplier
    /// - Q40 food: 2.0x multiplier
    /// - Q90 food: 3.0x multiplier
    /// </summary>
    public decimal CalculateEffectiveFep(decimal baseFep, int quality)
    {
        if (quality < 10) quality = 10;
        if (quality > 100) quality = 100;

        var qualityMultiplier = Math.Sqrt(quality / 10.0);
        return baseFep * (decimal)qualityMultiplier;
    }

    /// <summary>
    /// Apply quality multipliers to all FEPs in a food
    /// </summary>
    public FoodDto ApplyQualityMultiplier(FoodDto food, int quality)
    {
        var result = new FoodDto
        {
            Id = food.Id,
            TenantId = food.TenantId,
            Name = food.Name,
            ResourceType = food.ResourceType,
            Energy = food.Energy,
            Hunger = food.Hunger,
            Description = food.Description,
            IsSmoked = food.IsSmoked,
            SubmittedBy = food.SubmittedBy,
            CreatedAt = food.CreatedAt,
            IsVerified = food.IsVerified,
            Ingredients = food.Ingredients,
            Feps = food.Feps.Select(fep => new FoodFepDto
            {
                Id = fep.Id,
                AttributeName = fep.AttributeName,
                BaseValue = fep.BaseValue,
                EffectiveValue = CalculateEffectiveFep(fep.BaseValue, quality)
            }).ToList()
        };

        return result;
    }

    /// <summary>
    /// Calculate hunger bonus multiplier (based on current hunger level)
    /// HnH hunger system: 0-300% hunger, higher hunger = higher FEP gains
    /// </summary>
    public decimal CalculateHungerMultiplier(int hungerPercentage)
    {
        if (hungerPercentage < 0) hungerPercentage = 0;
        if (hungerPercentage > 300) hungerPercentage = 300;

        // Linear scaling from 1.0x at 0% to 3.0x at 300%
        return 1.0m + (hungerPercentage / 100.0m) * (2.0m / 3.0m);
    }
}
