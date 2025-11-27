namespace HnHMapperServer.Core.Models;

/// <summary>
/// Represents a Food Event Point (stat bonus) for a food
/// FEPs are attribute bonuses (STR, CON, AGI, INT, CHA, DEX, WILL, PSY, PER)
/// </summary>
public class FoodFep
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the Food this FEP belongs to
    /// </summary>
    public int FoodId { get; set; }

    /// <summary>
    /// Attribute name (e.g., "STR", "CON", "AGI", "INT", "CHA", "DEX", "WILL", "PSY", "PER")
    /// </summary>
    public string AttributeName { get; set; } = string.Empty;

    /// <summary>
    /// Base FEP value for this attribute (before quality multipliers)
    /// </summary>
    public decimal BaseValue { get; set; }

    /// <summary>
    /// Navigation property back to the Food
    /// </summary>
    public Food Food { get; set; } = null!;
}
