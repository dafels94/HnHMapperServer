namespace HnHMapperServer.Core.DTOs;

/// <summary>
/// DTO for food/recipe data transfer
/// </summary>
public class FoodDto
{
    public int Id { get; set; }
    public string? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public int Energy { get; set; }
    public int Hunger { get; set; }
    public string? Description { get; set; }
    public bool IsSmoked { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsVerified { get; set; }
    public bool IsGlobal => TenantId == null;

    // Navigation properties
    public List<FoodIngredientDto> Ingredients { get; set; } = new();
    public List<FoodFepDto> Feps { get; set; } = new();

    // Calculated metrics (populated by FoodMetricsCalculator)

    /// <summary>
    /// Global purity percentage (0.0 to 1.0). % of FEPs that are NOT CHA trash
    /// </summary>
    public decimal Purity { get; set; }

    /// <summary>
    /// Fighter purity percentage (0.0 to 1.0). % of useful FEPs that are STR/AGI/CON
    /// </summary>
    public decimal FighterPurity { get; set; }

    /// <summary>
    /// Crafter purity percentage (0.0 to 1.0). % of useful FEPs that are PSY/DEX/WILL
    /// </summary>
    public decimal CrafterPurity { get; set; }

    /// <summary>
    /// Universal purity percentage (0.0 to 1.0). % of useful FEPs that are INT/PER
    /// </summary>
    public decimal UniversalPurity { get; set; }

    /// <summary>
    /// Total CHA FEP value (trash stats that dilute food)
    /// </summary>
    public decimal ChaTrash { get; set; }

    /// <summary>
    /// Total useful FEP value (excluding CHA)
    /// </summary>
    public decimal UsefulFep { get; set; }

    /// <summary>
    /// Total of ALL FEPs (including CHA)
    /// </summary>
    public decimal TotalFep { get; set; }

    /// <summary>
    /// Fighter role score (STR+AGI+CON + universal×0.7)
    /// </summary>
    public decimal FighterScore { get; set; }

    /// <summary>
    /// Crafter role score (PSY+DEX+WILL + universal×0.7)
    /// </summary>
    public decimal CrafterScore { get; set; }

    /// <summary>
    /// Fighter role percentage (0-100)
    /// </summary>
    public decimal FighterPercent { get; set; }

    /// <summary>
    /// Crafter role percentage (0-100)
    /// </summary>
    public decimal CrafterPercent { get; set; }

    /// <summary>
    /// Power score (weighted combination of fighter and crafter scores)
    /// </summary>
    public decimal PowerScore { get; set; }

    /// <summary>
    /// Efficiency (PowerScore / Hunger)
    /// </summary>
    public decimal Efficiency { get; set; }

    /// <summary>
    /// Number of tier +2 FEPs
    /// </summary>
    public int Tier2Count { get; set; }

    /// <summary>
    /// Has rare PSY stat
    /// </summary>
    public bool HasPsy { get; set; }

    /// <summary>
    /// Food tier (Low, Mid, Best)
    /// </summary>
    public string Tier { get; set; } = "Low";

    /// <summary>
    /// Numeric tier value for calculation (0.0 = Low, 1.0 = Mid, 2.0 = Best)
    /// </summary>
    public decimal TierValue { get; set; }

    /// <summary>
    /// Calculated stat value for stat-specific queries (expected stat per eating)
    /// </summary>
    public decimal StatValue { get; set; }

    /// <summary>
    /// Concentration of target stat (0.0-1.0) for stat-specific queries
    /// </summary>
    public decimal StatConcentration { get; set; }

    /// <summary>
    /// Whether food has +2 tier for target stat
    /// </summary>
    public bool HasStatTier2 { get; set; }

    /// <summary>
    /// Expected stat gain per hunger point (StatPerEating / Hunger)
    /// Higher = more efficient. Used in stat research mode.
    /// </summary>
    public decimal StatEfficiency { get; set; }
}

/// <summary>
/// DTO for food ingredient
/// </summary>
public class FoodIngredientDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? Quality { get; set; }
}

/// <summary>
/// DTO for Food Event Point
/// </summary>
public class FoodFepDto
{
    public int Id { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public decimal BaseValue { get; set; }

    /// <summary>
    /// Calculated effective value based on quality (set by calculator service)
    /// </summary>
    public decimal? EffectiveValue { get; set; }
}

/// <summary>
/// DTO for submitting a new food/recipe from game client
/// </summary>
public class SubmitFoodDto
{
    public string Name { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public int Energy { get; set; }
    public int Hunger { get; set; }
    public string? Description { get; set; }
    public bool IsSmoked { get; set; }
    public bool IsGlobal { get; set; }

    public List<SubmitFoodIngredientDto> Ingredients { get; set; } = new();
    public List<SubmitFoodFepDto> Feps { get; set; } = new();
}

public class SubmitFoodIngredientDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? Quality { get; set; }
}

public class SubmitFoodFepDto
{
    public string AttributeName { get; set; } = string.Empty;
    public decimal BaseValue { get; set; }
}

/// <summary>
/// DTO for food search/filter request
/// </summary>
public class FoodSearchDto
{
    public string? SearchTerm { get; set; }
    public string? Ingredient { get; set; }
    public string? FepType { get; set; }
    public bool IncludeGlobal { get; set; } = true;
    public bool IncludeTenant { get; set; } = true;
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;

    // New filters for cookbook enhancement
    public decimal? MinPurity { get; set; }           // Minimum purity (0.0-1.0)
    public string? Tier { get; set; }                  // Low, Mid, Best
    public decimal? MinEfficiency { get; set; }
    public int? MaxHunger { get; set; }
    public string? SortBy { get; set; }                // "Efficiency", "Purity", "Name", etc.
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// DTO for food submission (pending approval)
/// </summary>
public class FoodSubmissionDto
{
    public int Id { get; set; }
    public string? TenantId { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public SubmitFoodDto Data { get; set; } = new();
    public string? ReviewNotes { get; set; }
}

/// <summary>
/// DTO for reviewing a food submission
/// </summary>
public class ReviewFoodSubmissionDto
{
    public bool Approve { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for verified contributor
/// </summary>
public class VerifiedContributorDto
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; }
    public string VerifiedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for grouped food variations (foods with same name grouped together)
/// </summary>
public class GroupedFoodDto
{
    /// <summary>
    /// Food name (base name without smoked suffix)
    /// </summary>
    public string FoodName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a smoked variant group
    /// </summary>
    public bool IsSmoked { get; set; }

    /// <summary>
    /// Number of variants in this group
    /// </summary>
    public int VariantCount { get; set; }

    /// <summary>
    /// Best variant by efficiency (representative food for collapsed view)
    /// </summary>
    public FoodDto BestVariant { get; set; } = new();

    /// <summary>
    /// All variants in this group (for expanded view)
    /// </summary>
    public List<FoodDto> AllVariants { get; set; } = new();
}

/// <summary>
/// DTO for stat analysis (all foods with a specific stat, grouped by tier and food name)
/// </summary>
public class StatAnalysisDto
{
    public string Stat { get; set; } = string.Empty;
    public int TotalFoods { get; set; }
    public List<GroupedFoodDto> BestTier { get; set; } = new();
    public List<GroupedFoodDto> MidTier { get; set; } = new();
    public List<GroupedFoodDto> LowTier { get; set; } = new();
    public List<GroupedFoodDto> TopOverall { get; set; } = new();
}

/// <summary>
/// DTO for feast planner results - shows hunger cost per +1 stat gain
/// </summary>
public class FeastPlannerResultDto
{
    public FoodDto Food { get; set; } = new();

    /// <summary>
    /// Total hunger needed to gain +1 stat point (lower = better)
    /// Calculated as: highestStat / StatEfficiency
    /// </summary>
    public decimal HungerPerStat { get; set; }
}

/// <summary>
/// DTO for feast planner response
/// </summary>
public class FeastPlannerResponseDto
{
    public string Stat { get; set; } = string.Empty;
    public int HighestStat { get; set; }
    public List<FeastPlannerResultDto> Results { get; set; } = new();
}
