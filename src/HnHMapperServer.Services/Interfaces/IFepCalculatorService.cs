using HnHMapperServer.Core.DTOs;

namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service for calculating Food Event Points (FEPs) with quality multipliers
/// </summary>
public interface IFepCalculatorService
{
    /// <summary>
    /// Calculate effective FEP value based on quality
    /// Formula: effectiveFep = baseFep * sqrt(quality / 10)
    /// </summary>
    /// <param name="baseFep">Base FEP value</param>
    /// <param name="quality">Quality level (10-100)</param>
    /// <returns>Calculated effective FEP</returns>
    decimal CalculateEffectiveFep(decimal baseFep, int quality);

    /// <summary>
    /// Apply quality multipliers to all FEPs in a food
    /// </summary>
    /// <param name="food">Food DTO with base FEPs</param>
    /// <param name="quality">Quality level to apply (10-100)</param>
    /// <returns>Food DTO with effective FEPs calculated</returns>
    FoodDto ApplyQualityMultiplier(FoodDto food, int quality);

    /// <summary>
    /// Calculate hunger bonus multiplier (based on current hunger level)
    /// </summary>
    /// <param name="hungerPercentage">Current hunger percentage (0-300)</param>
    /// <returns>Hunger multiplier (1.0x - 3.0x)</returns>
    decimal CalculateHungerMultiplier(int hungerPercentage);
}
