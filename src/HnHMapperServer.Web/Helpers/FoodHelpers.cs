using HnHMapperServer.Core.DTOs;
using MudBlazor;

namespace HnHMapperServer.Web.Helpers;

/// <summary>
/// Static helper class for food-related calculations and formatting.
/// Centralizes common logic used across cookbook components.
/// </summary>
public static class FoodHelpers
{
    /// <summary>
    /// Gets the MudBlazor color for a food tier.
    /// </summary>
    /// <param name="tier">Tier name (Best, Mid, Low)</param>
    /// <returns>MudBlazor Color enum value</returns>
    public static Color GetTierColor(string tier)
    {
        return tier switch
        {
            "Best" => Color.Success,
            "Mid" => Color.Warning,
            "Low" => Color.Default,
            _ => Color.Default
        };
    }

    /// <summary>
    /// Gets the emoji icon for a food tier.
    /// </summary>
    /// <param name="tier">Tier name (Best, Mid, Low)</param>
    /// <returns>Emoji string (🥇🥈🥉 or empty)</returns>
    public static string GetTierIcon(string tier)
    {
        return tier switch
        {
            "Best" => "🥇",
            "Mid" => "🥈",
            "Low" => "🥉",
            _ => ""
        };
    }

    /// <summary>
    /// Gets the MudBlazor color for a FEP attribute.
    /// </summary>
    /// <param name="attributeName">Attribute name (e.g., "STR +1", "AGI +2")</param>
    /// <returns>MudBlazor Color enum value</returns>
    public static Color GetFepColor(string attributeName)
    {
        var baseAttr = GetBaseStat(attributeName);

        return baseAttr.ToUpperInvariant() switch
        {
            "STR" => Color.Error,        // Red - Strength
            "AGI" => Color.Info,          // Blue - Agility
            "CON" => Color.Secondary,     // Purple - Constitution
            "INT" => Color.Primary,       // Light Blue - Intelligence
            "CHA" => Color.Success,       // Green - Charisma
            "DEX" => Color.Warning,       // Yellow/Orange - Dexterity
            "WILL" => Color.Dark,         // Dark - Will
            "PSY" => Color.Tertiary,      // Pink/Magenta - Psyche
            "PER" => Color.Warning,       // Orange - Perception
            _ => Color.Default
        };
    }

    /// <summary>
    /// Gets the hex color style for a FEP attribute.
    /// Colors match hnh-food-book reference implementation.
    /// </summary>
    /// <param name="attributeName">Attribute name (e.g., "STR +1", "AGI +2")</param>
    /// <returns>Hex color string</returns>
    public static string GetFepColorStyle(string attributeName)
    {
        var baseAttr = GetBaseStat(attributeName);

        return baseAttr.ToUpperInvariant() switch
        {
            "STR" => "#DF958F",     // Salmon - Strength
            "AGI" => "#9991DC",     // Lavender - Agility
            "INT" => "#97D6DC",     // Teal - Intelligence
            "CON" => "#E193C5",     // Pink - Constitution
            "PER" => "#F2C28D",     // Peach - Perception
            "CHA" => "#8EF7AA",     // Mint - Charisma
            "DEX" => "#FFFEA6",     // Yellow - Dexterity
            "WILL" => "#EEFF9E",    // Lime - Will
            "PSY" => "#C286FE",     // Violet - Psyche
            _ => "#9e9e9e"          // Gray - Default
        };
    }

    /// <summary>
    /// Gets the display order for a FEP attribute.
    /// Order matches hnh-food-book reference implementation.
    /// </summary>
    /// <param name="attributeName">Attribute name (e.g., "STR +1", "AGI +2")</param>
    /// <returns>Sort order (1-9, 99 for unknown)</returns>
    public static int GetStatOrder(string attributeName)
    {
        var baseAttr = GetBaseStat(attributeName);

        return baseAttr.ToUpperInvariant() switch
        {
            "STR" => 1,
            "AGI" => 2,
            "INT" => 3,
            "CON" => 4,
            "PER" => 5,
            "CHA" => 6,
            "DEX" => 7,
            "WILL" => 8,
            "PSY" => 9,
            _ => 99
        };
    }

    /// <summary>
    /// Gets the MudBlazor color for a purity percentage.
    /// </summary>
    /// <param name="purity">Purity value (0.0 to 1.0)</param>
    /// <returns>MudBlazor Color enum value</returns>
    public static Color GetPurityColor(decimal purity)
    {
        if (purity >= 0.90m) return Color.Success;
        if (purity >= 0.70m) return Color.Info;
        if (purity >= 0.50m) return Color.Warning;
        return Color.Error;
    }

    /// <summary>
    /// Extracts the base stat name from an attribute name (removes tier suffix).
    /// </summary>
    /// <param name="attributeName">Attribute name (e.g., "STR +1", "AGI +2")</param>
    /// <returns>Base stat name (e.g., "STR", "AGI")</returns>
    public static string GetBaseStat(string attributeName)
    {
        return attributeName.Split('+')[0].Trim();
    }

    /// <summary>
    /// Gets the tier multiplier for stat gain calculations.
    /// </summary>
    /// <param name="attributeName">Attribute name (e.g., "STR +1", "AGI +2")</param>
    /// <returns>Multiplier (2 for +2 tier, 1 otherwise)</returns>
    public static int GetTierMultiplier(string attributeName)
    {
        if (attributeName.Contains("+2")) return 2;
        return 1;
    }

    /// <summary>
    /// Calculates tier-adjusted efficiency with concentration bonus for a food.
    /// </summary>
    /// <param name="food">Food DTO with FEPs and hunger</param>
    /// <returns>Efficiency value (expected stats per hunger × concentration bonus)</returns>
    public static decimal CalculateTierAdjustedEfficiency(FoodDto food)
    {
        if (!food.Feps.Any() || food.Hunger == 0)
            return 0;

        var totalFep = food.Feps.Sum(f => (double)f.BaseValue);
        if (totalFep == 0)
            return 0;

        // Step 1: Group by base stat to find dominant stat percentage
        var groupedByBaseStat = food.Feps
            .GroupBy(f => GetBaseStat(f.AttributeName))
            .Select(g => new { BaseStat = g.Key, TotalFep = g.Sum(f => (double)f.BaseValue) })
            .OrderByDescending(g => g.TotalFep)
            .ToList();

        var dominantStatPercent = (groupedByBaseStat.First().TotalFep / totalFep) * 100;

        // Step 2: Calculate tier-adjusted expected stat value
        double expectedStatValue = 0;
        foreach (var fep in food.Feps)
        {
            var probability = (double)fep.BaseValue / totalFep;
            var tierMultiplier = GetTierMultiplier(fep.AttributeName);
            expectedStatValue += probability * tierMultiplier;
        }

        // Step 3: Apply concentration bonus
        var baseEfficiency = expectedStatValue / food.Hunger;
        var concentrationBonus = 1 + (dominantStatPercent / 100);

        return (decimal)(baseEfficiency * concentrationBonus);
    }

    /// <summary>
    /// Calculates the dominant stat and its percentage for a food.
    /// </summary>
    /// <param name="food">Food DTO with FEPs</param>
    /// <returns>Tuple of (dominant stat name, percentage, first FEP for color)</returns>
    public static (string BaseStat, double Percentage, FoodFepDto FirstFep)? GetDominantStat(FoodDto food)
    {
        if (!food.Feps.Any())
            return null;

        var totalFep = food.Feps.Sum(f => (double)f.BaseValue);
        if (totalFep == 0)
            return null;

        var groupedByBaseStat = food.Feps
            .GroupBy(f => GetBaseStat(f.AttributeName))
            .Select(g => new
            {
                BaseStat = g.Key,
                TotalFep = g.Sum(f => (double)f.BaseValue),
                FirstFep = g.First()
            })
            .OrderByDescending(g => g.TotalFep)
            .FirstOrDefault();

        if (groupedByBaseStat == null)
            return null;

        var percentage = (groupedByBaseStat.TotalFep / totalFep) * 100;
        return (groupedByBaseStat.BaseStat, percentage, groupedByBaseStat.FirstFep);
    }
}
