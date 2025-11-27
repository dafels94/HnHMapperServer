using System.Text.RegularExpressions;
using HnHMapperServer.Core.DTOs;
using Microsoft.Extensions.Logging;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Calculates food metrics according to the Haven & Hearth food categorization algorithm.
/// Implements purity tracking (CHA as trash stat), role scoring, and dynamic tier assignment.
/// </summary>
public class FoodMetricsCalculator
{
    private readonly ILogger<FoodMetricsCalculator> _logger;

    // Configuration (research-proven optimal values)
    private const decimal UniversalFighterWeight = 0.7m;
    private const decimal UniversalCrafterWeight = 0.7m;
    private const decimal Tier2Multiplier = 1.5m;
    private const decimal PsyBonus = 0.5m;
    private const decimal HighPurityBonus = 0.5m;
    private const decimal PurityThreshold = 0.90m;
    private const decimal BestPercentile = 0.20m;  // Top 20%
    private const decimal MidPercentile = 0.60m;   // Top 60%
    private const int HungerPenaltyThreshold = 15;

    // Attribute categorization
    private static readonly HashSet<string> PureFighterAttributes = new() { "STR", "AGI", "CON" };
    private static readonly HashSet<string> PureCrafterAttributes = new() { "PSY", "DEX", "WILL" };
    private static readonly HashSet<string> UniversalAttributes = new() { "INT", "PER" };
    private const string TrashAttribute = "CHA";  // CHA is trash!

    public FoodMetricsCalculator(ILogger<FoodMetricsCalculator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate metrics for a single food
    /// </summary>
    public void CalculateFoodMetrics(FoodDto food, decimal fighterPreference = 0.5m, decimal crafterPreference = 0.5m)
    {
        // Step 1: Calculate raw scores (IGNORE CHA!)
        var raw = CalculateRawScores(food);

        // Store raw metrics
        food.Purity = raw.Purity;
        food.FighterPurity = raw.FighterPurity;
        food.CrafterPurity = raw.CrafterPurity;
        food.UniversalPurity = raw.UniversalPurity;
        food.ChaTrash = raw.ChaTrash;
        food.UsefulFep = raw.UsefulFep;
        food.TotalFep = raw.TotalFep;
        food.Tier2Count = raw.Tier2Count;
        food.HasPsy = raw.HasPsy;

        // Step 2: Calculate role scores
        food.FighterScore = raw.PureFighter + (raw.Universal * UniversalFighterWeight);
        food.CrafterScore = raw.PureCrafter + (raw.Universal * UniversalCrafterWeight);

        // Step 3: Calculate role percentages
        var totalRoleScore = food.FighterScore + food.CrafterScore;
        if (totalRoleScore > 0)
        {
            food.FighterPercent = (food.FighterScore / totalRoleScore) * 100;
            food.CrafterPercent = (food.CrafterScore / totalRoleScore) * 100;
        }

        // Step 4: Calculate power score
        var totalPref = fighterPreference + crafterPreference;
        var fWeight = fighterPreference / totalPref;
        var cWeight = crafterPreference / totalPref;
        food.PowerScore = (food.FighterScore * fWeight) + (food.CrafterScore * cWeight);

        // Step 5: Calculate efficiency
        food.Efficiency = food.Hunger > 0 ? food.PowerScore / food.Hunger : food.PowerScore * 1000;
    }

    /// <summary>
    /// Assign tiers to a collection of foods based on role-aware percentile ranking.
    /// Foods are categorized by role (Fighter/Crafter/Universal) and tiered within their category.
    /// Uses role-specific purity for tier bonuses.
    /// </summary>
    public void AssignTiers(List<FoodDto> foods)
    {
        if (foods.Count == 0) return;

        // Separate foods by role category
        var fighterFoods = foods.Where(f => f.FighterPercent > 60).ToList();
        var crafterFoods = foods.Where(f => f.CrafterPercent > 60).ToList();
        var universalFoods = foods.Where(f =>
            f.FighterPercent >= 40 && f.FighterPercent <= 60 &&
            f.CrafterPercent >= 40 && f.CrafterPercent <= 60
        ).ToList();

        // Assign tiers within each role category
        AssignRoleTiers(fighterFoods, "Fighter");
        AssignRoleTiers(crafterFoods, "Crafter");
        AssignRoleTiers(universalFoods, "Universal");
    }

    /// <summary>
    /// Assign tiers to foods within a specific role category
    /// </summary>
    private void AssignRoleTiers(List<FoodDto> foods, string role)
    {
        if (foods.Count == 0) return;

        // Sort by efficiency (descending)
        var sorted = foods.OrderByDescending(f => f.Efficiency).ToList();

        // Calculate cutoff indices (within this role category)
        var bestIdx = Math.Max(0, (int)(foods.Count * BestPercentile) - 1);
        var midIdx = Math.Max(0, (int)(foods.Count * MidPercentile) - 1);

        var bestCutoff = sorted[bestIdx].Efficiency;
        var midCutoff = sorted[midIdx].Efficiency;

        // Assign base tiers
        foreach (var food in foods)
        {
            if (food.Efficiency >= bestCutoff)
                food.TierValue = 2.0m;
            else if (food.Efficiency >= midCutoff)
                food.TierValue = 1.0m;
            else
                food.TierValue = 0.0m;
        }

        // Apply role-specific modifiers
        foreach (var food in foods)
        {
            decimal adjustment = 0;

            // Get role-specific purity for this food
            var rolePurity = role switch
            {
                "Fighter" => food.FighterPurity,
                "Crafter" => food.CrafterPurity,
                "Universal" => food.UniversalPurity,
                _ => food.Purity  // Fallback to global purity
            };

            // Role purity bonuses/penalties
            if (rolePurity >= 0.95m)
                adjustment += 1.0m;  // 95%+ purity = huge bonus
            else if (rolePurity >= 0.85m)
                adjustment += 0.5m;  // 85-94% = moderate bonus
            else if (rolePurity < 0.50m)
                adjustment -= 0.5m;  // <50% purity = penalty (wasted FEPs)

            // Boost: 2+ tier +2 FEPs
            if (food.Tier2Count >= 2)
                adjustment += 1.0m;

            // Boost: Has PSY (rare stat)
            if (food.HasPsy)
                adjustment += PsyBonus;

            // Penalty: Very high hunger
            if (food.Hunger > HungerPenaltyThreshold)
                adjustment -= 1.0m;

            // Apply and clamp
            food.TierValue += adjustment;
            food.TierValue = Math.Max(0.0m, Math.Min(2.0m, food.TierValue));

            // Map to tier name
            food.Tier = food.TierValue switch
            {
                >= 1.75m => "Best",
                >= 0.75m => "Mid",
                _ => "Low"
            };
        }
    }

    /// <summary>
    /// Calculate raw scores from FEPs
    /// </summary>
    private RawScores CalculateRawScores(FoodDto food)
    {
        decimal pureFighter = 0;
        decimal pureCrafter = 0;
        decimal universal = 0;
        decimal chaTrash = 0;
        decimal totalFep = 0;
        int tier2Count = 0;
        bool hasPsy = false;

        // Track raw (unweighted) values for purity calculation
        decimal rawFighterFep = 0;
        decimal rawCrafterFep = 0;
        decimal rawUniversalFep = 0;

        foreach (var fep in food.Feps)
        {
            var attribute = ExtractAttribute(fep.AttributeName);
            var tier = ExtractTier(fep.AttributeName);
            var baseValue = fep.BaseValue;

            // Track total including trash
            totalFep += baseValue;

            // Apply tier weighting
            var weightedValue = tier == 2 ? baseValue * Tier2Multiplier : baseValue;
            if (tier == 2) tier2Count++;

            // Categorize (IGNORE CHA!)
            if (attribute == TrashAttribute)
            {
                chaTrash += baseValue;
                continue;  // Skip CHA entirely!
            }

            if (PureFighterAttributes.Contains(attribute))
            {
                pureFighter += weightedValue;  // Tier-weighted for scoring
                rawFighterFep += baseValue;     // Raw for purity
            }
            else if (PureCrafterAttributes.Contains(attribute))
            {
                pureCrafter += weightedValue;
                rawCrafterFep += baseValue;
                if (attribute == "PSY") hasPsy = true;
            }
            else if (UniversalAttributes.Contains(attribute))
            {
                universal += weightedValue;
                rawUniversalFep += baseValue;
            }
        }

        // Calculate global purity (% non-CHA)
        var usefulFep = totalFep - chaTrash;
        var purity = totalFep > 0 ? usefulFep / totalFep : 1.0m;

        // Calculate role-specific purities (% of useful FEPs that are role-specific)
        var fighterPurity = usefulFep > 0 ? rawFighterFep / usefulFep : 0m;
        var crafterPurity = usefulFep > 0 ? rawCrafterFep / usefulFep : 0m;
        var universalPurity = usefulFep > 0 ? rawUniversalFep / usefulFep : 0m;

        return new RawScores
        {
            PureFighter = pureFighter,
            PureCrafter = pureCrafter,
            Universal = universal,
            ChaTrash = chaTrash,
            UsefulFep = usefulFep,
            TotalFep = totalFep,
            Purity = purity,
            FighterPurity = fighterPurity,
            CrafterPurity = crafterPurity,
            UniversalPurity = universalPurity,
            Tier2Count = tier2Count,
            HasPsy = hasPsy
        };
    }

    /// <summary>
    /// Extract attribute name from FEP string (e.g., "STR +2" -> "STR")
    /// Case-insensitive to handle variations in database format.
    /// </summary>
    private string ExtractAttribute(string fepName)
    {
        // Made case-insensitive to handle "STR", "str", "Str", etc.
        var match = Regex.Match(fepName, @"^([A-Z]+)", RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            _logger.LogWarning("Failed to extract attribute from FEP name: '{FepName}'. Expected format: 'ATTR +N' (e.g., 'STR +2')", fepName);
            return string.Empty;
        }

        var attribute = match.Groups[1].Value.ToUpper();  // Normalize to uppercase

        // Check if this is a recognized attribute
        if (!PureFighterAttributes.Contains(attribute) &&
            !PureCrafterAttributes.Contains(attribute) &&
            !UniversalAttributes.Contains(attribute) &&
            attribute != TrashAttribute)
        {
            _logger.LogWarning("Unrecognized FEP attribute: '{Attribute}' from '{FepName}'. This FEP will be ignored in scoring.",
                attribute, fepName);
        }

        return attribute;
    }

    /// <summary>
    /// Extract tier from FEP string (e.g., "STR +2" -> 2)
    /// </summary>
    private int ExtractTier(string fepName)
    {
        var match = Regex.Match(fepName, @"\+(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 1;
    }

    /// <summary>
    /// Internal structure for raw score calculation
    /// </summary>
    private class RawScores
    {
        public decimal PureFighter { get; set; }
        public decimal PureCrafter { get; set; }
        public decimal Universal { get; set; }
        public decimal ChaTrash { get; set; }
        public decimal UsefulFep { get; set; }
        public decimal TotalFep { get; set; }
        public decimal Purity { get; set; }  // Global purity (non-CHA %)
        public decimal FighterPurity { get; set; }  // % STR/AGI/CON
        public decimal CrafterPurity { get; set; }  // % PSY/DEX/WILL
        public decimal UniversalPurity { get; set; }  // % INT/PER
        public int Tier2Count { get; set; }
        public bool HasPsy { get; set; }
    }
}
