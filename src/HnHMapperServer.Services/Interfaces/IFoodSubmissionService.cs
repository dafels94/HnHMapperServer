using HnHMapperServer.Core.DTOs;

namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service for managing food submission workflow (pending recipes awaiting approval)
/// </summary>
public interface IFoodSubmissionService
{
    /// <summary>
    /// Submit a new food/recipe (creates submission or publishes directly if verified)
    /// </summary>
    Task<(bool Published, int? FoodId, int? SubmissionId)> SubmitFoodAsync(SubmitFoodDto foodDto, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pending submissions for the current tenant
    /// </summary>
    Task<List<FoodSubmissionDto>> GetPendingSubmissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific submission by ID
    /// </summary>
    Task<FoodSubmissionDto?> GetSubmissionByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a submission (creates food and marks submission as approved)
    /// </summary>
    Task<FoodDto?> ApproveSubmissionAsync(int submissionId, string reviewerId, string? notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reject a submission
    /// </summary>
    Task<bool> RejectSubmissionAsync(int submissionId, string reviewerId, string? notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old/stale submissions (for background cleanup service)
    /// </summary>
    Task<int> DeleteStaleSubmissionsAsync(int daysOld = 7, CancellationToken cancellationToken = default);
}
