using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HnHMapperServer.Api.Endpoints;

/// <summary>
/// Public API endpoints for cookbook food/recipe management
/// </summary>
public static class FoodEndpoints
{
    public static void MapFoodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/foods");

        // Public endpoints (read-only)
        group.MapGet("", GetAllFoods);
        group.MapPost("/search", SearchFoods);
        group.MapGet("/{id:int}", GetFoodById);

        // Overview endpoints
        group.MapGet("/top-by-stat/{stat}", GetTopFoodsByStat);
        group.MapGet("/top-by-role/{role}", GetTopFoodsByRole);
        group.MapGet("/stat-analysis/{stat}", GetStatAnalysis);

        // Feast planner endpoint
        group.MapGet("/feast-planner", GetFeastPlanner);

        // FEP calculator endpoint
        group.MapPost("/calculate-feps", CalculateFeps);
    }

    /// <summary>
    /// GET /api/foods - Get all foods (global + tenant-specific)
    /// </summary>
    private static async Task<IResult> GetAllFoods(
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            var foods = await foodService.GetAllFoodsAsync(cancellationToken);
            return Results.Ok(foods);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve foods: {ex.Message}");
        }
    }

    /// <summary>
    /// POST /api/foods/search - Search and filter foods
    /// </summary>
    private static async Task<IResult> SearchFoods(
        [FromBody] FoodSearchDto search,
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            var (foods, totalCount) = await foodService.SearchFoodsAsync(search, cancellationToken);
            return Results.Ok(new { foods, totalCount });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to search foods: {ex.Message}");
        }
    }

    /// <summary>
    /// GET /api/foods/{id} - Get a specific food by ID
    /// </summary>
    private static async Task<IResult> GetFoodById(
        [FromRoute] int id,
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            var food = await foodService.GetFoodByIdAsync(id, cancellationToken);
            return food != null ? Results.Ok(food) : Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve food: {ex.Message}");
        }
    }

    /// <summary>
    /// GET /api/foods/top-by-stat/{stat}?limit=3 - Get top foods for a specific stat (e.g., STR, AGI, PSY)
    /// </summary>
    private static async Task<IResult> GetTopFoodsByStat(
        [FromRoute] string stat,
        [FromQuery] int limit,
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            // Default to 3 if not specified
            if (limit <= 0) limit = 3;

            var foods = await foodService.GetTopFoodsByStatAsync(stat, limit, cancellationToken);
            return Results.Ok(foods);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve top foods by stat: {ex.Message}");
        }
    }

    /// <summary>
    /// GET /api/foods/top-by-role/{role}?limit=20 - Get top foods for a specific role (Fighter, Crafter, Universal)
    /// </summary>
    private static async Task<IResult> GetTopFoodsByRole(
        [FromRoute] string role,
        [FromQuery] int limit,
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            // Default to 20 if not specified
            if (limit <= 0) limit = 20;

            var foods = await foodService.GetTopFoodsByRoleAsync(role, limit, cancellationToken);
            return Results.Ok(foods);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve top foods by role: {ex.Message}");
        }
    }

    /// <summary>
    /// GET /api/foods/stat-analysis/{stat} - Get comprehensive stat analysis (all foods with that stat, grouped by tier)
    /// </summary>
    private static async Task<IResult> GetStatAnalysis(
        [FromRoute] string stat,
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await foodService.GetStatAnalysisAsync(stat, cancellationToken);
            return Results.Ok(analysis);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve stat analysis: {ex.Message}");
        }
    }

    /// <summary>
    /// GET /api/foods/feast-planner?stat=AGI&highestStat=500&limit=20 - Get feast planner results
    /// Shows hunger cost per +1 stat gain for optimal food selection
    /// </summary>
    private static async Task<IResult> GetFeastPlanner(
        [FromQuery] string stat,
        [FromQuery] int highestStat,
        [FromQuery] int limit,
        IFoodService foodService,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stat))
                return Results.BadRequest("Stat parameter is required");

            if (highestStat <= 0)
                return Results.BadRequest("HighestStat must be greater than 0");

            // Default limit to 20 if not specified
            if (limit <= 0) limit = 20;

            var result = await foodService.GetFeastPlannerAsync(stat, highestStat, limit, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve feast planner results: {ex.Message}");
        }
    }

    /// <summary>
    /// POST /api/foods/calculate-feps - Calculate effective FEPs with quality multiplier
    /// </summary>
    private static IResult CalculateFeps(
        [FromBody] CalculateFepsRequest request,
        IFepCalculatorService fepCalculatorService)
    {
        try
        {
            if (request.Food == null)
                return Results.BadRequest("Food data is required");

            var result = fepCalculatorService.ApplyQualityMultiplier(request.Food, request.Quality);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to calculate FEPs: {ex.Message}");
        }
    }
}

/// <summary>
/// Request model for FEP calculation
/// </summary>
public class CalculateFepsRequest
{
    public FoodDto? Food { get; set; }
    public int Quality { get; set; } = 10;
}
