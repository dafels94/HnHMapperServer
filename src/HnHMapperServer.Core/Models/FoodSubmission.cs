namespace HnHMapperServer.Core.Models;

/// <summary>
/// Represents a pending food/recipe submission awaiting approval
/// Used for users who are not verified contributors
/// </summary>
public class FoodSubmission
{
    public int Id { get; set; }

    /// <summary>
    /// Tenant ID for the submission (null for global submission requests)
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// User ID who submitted this recipe
    /// </summary>
    public string SubmittedBy { get; set; } = string.Empty;

    /// <summary>
    /// When the submission was created
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Submission status (Pending, Approved, Rejected)
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// JSON payload containing the food data (name, ingredients, FEPs, etc.)
    /// Stored as JSON to avoid creating partial Food entities
    /// </summary>
    public string DataJson { get; set; } = string.Empty;

    /// <summary>
    /// User ID who reviewed this submission
    /// </summary>
    public string? ReviewedBy { get; set; }

    /// <summary>
    /// When the submission was reviewed
    /// </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Admin comments/notes about the review decision
    /// </summary>
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// ID of the created Food if approved
    /// </summary>
    public int? ApprovedFoodId { get; set; }
}
