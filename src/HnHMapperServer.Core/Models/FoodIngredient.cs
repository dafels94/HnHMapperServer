namespace HnHMapperServer.Core.Models;

/// <summary>
/// Represents an ingredient in a food recipe
/// </summary>
public class FoodIngredient
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the Food this ingredient belongs to
    /// </summary>
    public int FoodId { get; set; }

    /// <summary>
    /// Name of the ingredient (e.g., "Wheat", "Water", "Salt")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Quantity or percentage of this ingredient in the recipe
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Optional quality level of the ingredient (for FEP calculations)
    /// </summary>
    public int? Quality { get; set; }

    /// <summary>
    /// Navigation property back to the Food
    /// </summary>
    public Food Food { get; set; } = null!;
}
