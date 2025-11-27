using HnHMapperServer.Core.DTOs;

namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service for managing verified contributors (users trusted to auto-publish recipes)
/// </summary>
public interface IVerifiedContributorService
{
    /// <summary>
    /// Get all verified contributors in the current tenant
    /// </summary>
    Task<List<VerifiedContributorDto>> GetAllVerifiedContributorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user is a verified contributor in the current tenant
    /// </summary>
    Task<bool> IsVerifiedContributorAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a verified contributor
    /// </summary>
    Task<VerifiedContributorDto> AddVerifiedContributorAsync(string userId, string verifiedBy, string? notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a verified contributor
    /// </summary>
    Task<bool> RemoveVerifiedContributorAsync(string userId, CancellationToken cancellationToken = default);
}
