namespace HnHMapperServer.Core.Models;

/// <summary>
/// Represents a food/recipe in the Haven & Hearth cookbook.
/// Can be global (TenantId = null) or tenant-specific.
/// </summary>
public class Food
{
    public int Id { get; set; }

    /// <summary>
    /// Null for global recipes, specific tenant ID for tenant-private recipes
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Display name of the food/recipe
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Game resource type identifier (e.g., "gfx/invobjs/bread")
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Energy value provided by this food
    /// </summary>
    public int Energy { get; set; }

    /// <summary>
    /// Hunger value (how much hunger it fills)
    /// </summary>
    public int Hunger { get; set; }

    /// <summary>
    /// Optional description or notes about the recipe
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is a smoked variant of a food
    /// </summary>
    public bool IsSmoked { get; set; }

    /// <summary>
    /// User ID who submitted this recipe
    /// </summary>
    public string SubmittedBy { get; set; } = string.Empty;

    /// <summary>
    /// When the recipe was created/submitted
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// User ID who approved this recipe (if applicable)
    /// </summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// When the recipe was approved (if applicable)
    /// </summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Whether this recipe has been verified/approved
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// List of ingredients for this recipe
    /// </summary>
    public List<FoodIngredient> Ingredients { get; set; } = new();

    /// <summary>
    /// List of Food Event Points (FEPs) for stat bonuses
    /// </summary>
    public List<FoodFep> Feps { get; set; } = new();
}
