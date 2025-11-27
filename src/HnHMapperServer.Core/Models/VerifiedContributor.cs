namespace HnHMapperServer.Core.Models;

/// <summary>
/// Represents a user who is trusted to auto-publish recipes without approval
/// Tenant-scoped: users can be verified in some tenants but not others
/// </summary>
public class VerifiedContributor
{
    public int Id { get; set; }

    /// <summary>
    /// Tenant ID where this user is verified
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// User ID of the verified contributor
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// When the user was granted verified status
    /// </summary>
    public DateTime VerifiedAt { get; set; }

    /// <summary>
    /// Admin user ID who granted verified status
    /// </summary>
    public string VerifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// Optional notes about why this user was verified
    /// </summary>
    public string? Notes { get; set; }
}
