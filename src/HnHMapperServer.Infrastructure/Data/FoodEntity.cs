namespace HnHMapperServer.Infrastructure.Data;

/// <summary>
/// Entity for Food table - represents a food/recipe in the cookbook
/// </summary>
public sealed class FoodEntity
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
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsVerified { get; set; }

    public List<FoodIngredientEntity> Ingredients { get; set; } = new();
    public List<FoodFepEntity> Feps { get; set; } = new();
}

/// <summary>
/// Entity for FoodIngredients table
/// </summary>
public sealed class FoodIngredientEntity
{
    public int Id { get; set; }
    public int FoodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? Quality { get; set; }

    public FoodEntity Food { get; set; } = null!;
}

/// <summary>
/// Entity for FoodFeps table
/// </summary>
public sealed class FoodFepEntity
{
    public int Id { get; set; }
    public int FoodId { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public decimal BaseValue { get; set; }

    public FoodEntity Food { get; set; } = null!;
}

/// <summary>
/// Entity for FoodSubmissions table - pending recipes awaiting approval
/// </summary>
public sealed class FoodSubmissionEntity
{
    public int Id { get; set; }
    public string? TenantId { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string DataJson { get; set; } = string.Empty;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public int? ApprovedFoodId { get; set; }
}

/// <summary>
/// Entity for VerifiedContributors table - users trusted to auto-publish recipes
/// </summary>
public sealed class VerifiedContributorEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; }
    public string VerifiedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
