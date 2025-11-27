using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Models;

namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service for managing food/recipe data in the cookbook
/// </summary>
public interface IFoodService
{
    /// <summary>
    /// Get all foods (global + tenant-specific)
    /// </summary>
    Task<List<FoodDto>> GetAllFoodsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Search and filter foods
    /// </summary>
    Task<(List<FoodDto> Foods, int TotalCount)> SearchFoodsAsync(FoodSearchDto search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single food by ID
    /// </summary>
    Task<FoodDto?> GetFoodByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new food (admin or verified contributor)
    /// </summary>
    Task<FoodDto> CreateFoodAsync(SubmitFoodDto foodDto, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing food (admin only)
    /// </summary>
    Task<FoodDto?> UpdateFoodAsync(int id, SubmitFoodDto foodDto, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a food (admin only)
    /// </summary>
    Task<bool> DeleteFoodAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user is a verified contributor in the current tenant
    /// </summary>
    Task<bool> IsVerifiedContributorAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get top foods for a specific stat (e.g., STR, AGI, PSY)
    /// </summary>
    Task<List<FoodDto>> GetTopFoodsByStatAsync(string stat, int limit = 3, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get top foods for a specific role (Fighter, Crafter, Universal), grouped by food name
    /// </summary>
    Task<List<GroupedFoodDto>> GetTopFoodsByRoleAsync(string role, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get comprehensive stat analysis for a specific stat (e.g., STR, AGI, PSY)
    /// Returns all foods with that stat, grouped by tier
    /// </summary>
    Task<StatAnalysisDto> GetStatAnalysisAsync(string stat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Groups foods by name (including smoked suffix) and selects best variant by efficiency
    /// </summary>
    /// <param name="foods">List of foods to group</param>
    /// <returns>List of grouped foods with best variant selected</returns>
    List<GroupedFoodDto> GroupFoodsByName(List<FoodDto> foods);

    /// <summary>
    /// Get feast planner results - shows hunger cost per +1 stat gain
    /// </summary>
    /// <param name="stat">Target stat (STR, AGI, etc.)</param>
    /// <param name="highestStat">User's highest stat value (FEPs needed for +1 stat)</param>
    /// <param name="limit">Max results to return</param>
    Task<FeastPlannerResponseDto> GetFeastPlannerAsync(string stat, int highestStat, int limit = 20, CancellationToken cancellationToken = default);
}
