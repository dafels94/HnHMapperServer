using System.Text.Json;
using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for managing food submission workflow (pending recipes awaiting approval)
/// </summary>
public class FoodSubmissionService : IFoodSubmissionService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IFoodService _foodService;
    private readonly IVerifiedContributorService _verifiedContributorService;

    public FoodSubmissionService(
        ApplicationDbContext context,
        ITenantContextAccessor tenantContext,
        IFoodService foodService,
        IVerifiedContributorService verifiedContributorService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _foodService = foodService;
        _verifiedContributorService = verifiedContributorService;
    }

    public async Task<(bool Published, int? FoodId, int? SubmissionId)> SubmitFoodAsync(SubmitFoodDto foodDto, string userId, CancellationToken cancellationToken = default)
    {
        // Check if user is verified contributor
        var isVerified = await _verifiedContributorService.IsVerifiedContributorAsync(userId, cancellationToken);

        if (isVerified)
        {
            // Auto-publish for verified contributors
            var food = await _foodService.CreateFoodAsync(foodDto, userId, cancellationToken);
            return (true, food.Id, null);
        }
        else
        {
            // Create submission for approval
            var tenantId = foodDto.IsGlobal ? null : _tenantContext.GetCurrentTenantId();

            var submission = new FoodSubmissionEntity
            {
                TenantId = tenantId,
                SubmittedBy = userId,
                SubmittedAt = DateTime.UtcNow,
                Status = "Pending",
                DataJson = JsonSerializer.Serialize(foodDto)
            };

            _context.FoodSubmissions.Add(submission);
            await _context.SaveChangesAsync(cancellationToken);

            return (false, null, submission.Id);
        }
    }

    public async Task<List<FoodSubmissionDto>> GetPendingSubmissionsAsync(CancellationToken cancellationToken = default)
    {
        var submissions = await _context.FoodSubmissions
            .Where(s => s.Status == "Pending")
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync(cancellationToken);

        return submissions.Select(MapToDto).ToList();
    }

    public async Task<FoodSubmissionDto?> GetSubmissionByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var submission = await _context.FoodSubmissions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return submission != null ? MapToDto(submission) : null;
    }

    public async Task<FoodDto?> ApproveSubmissionAsync(int submissionId, string reviewerId, string? notes, CancellationToken cancellationToken = default)
    {
        var submission = await _context.FoodSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission == null || submission.Status != "Pending")
        {
            return null;
        }

        // Deserialize food data
        var foodDto = JsonSerializer.Deserialize<SubmitFoodDto>(submission.DataJson);
        if (foodDto == null)
        {
            throw new InvalidOperationException("Failed to deserialize food data");
        }

        // Create the food
        var food = await _foodService.CreateFoodAsync(foodDto, submission.SubmittedBy, cancellationToken);

        // Update submission
        submission.Status = "Approved";
        submission.ReviewedBy = reviewerId;
        submission.ReviewedAt = DateTime.UtcNow;
        submission.ReviewNotes = notes;
        submission.ApprovedFoodId = food.Id;

        await _context.SaveChangesAsync(cancellationToken);

        return food;
    }

    public async Task<bool> RejectSubmissionAsync(int submissionId, string reviewerId, string? notes, CancellationToken cancellationToken = default)
    {
        var submission = await _context.FoodSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission == null || submission.Status != "Pending")
        {
            return false;
        }

        submission.Status = "Rejected";
        submission.ReviewedBy = reviewerId;
        submission.ReviewedAt = DateTime.UtcNow;
        submission.ReviewNotes = notes;

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<int> DeleteStaleSubmissionsAsync(int daysOld = 7, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);

        var staleSubmissions = await _context.FoodSubmissions
            .Where(s => s.Status == "Pending" && s.SubmittedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        if (staleSubmissions.Count == 0)
        {
            return 0;
        }

        _context.FoodSubmissions.RemoveRange(staleSubmissions);
        await _context.SaveChangesAsync(cancellationToken);

        return staleSubmissions.Count;
    }

    private static FoodSubmissionDto MapToDto(FoodSubmissionEntity entity)
    {
        var foodData = JsonSerializer.Deserialize<SubmitFoodDto>(entity.DataJson)
                       ?? new SubmitFoodDto();

        return new FoodSubmissionDto
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            SubmittedBy = entity.SubmittedBy,
            SubmittedAt = entity.SubmittedAt,
            Status = entity.Status,
            Data = foodData,
            ReviewNotes = entity.ReviewNotes
        };
    }
}
