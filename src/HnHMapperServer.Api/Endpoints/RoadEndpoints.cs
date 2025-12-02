using System.Security.Claims;
using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Enums;
using HnHMapperServer.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using HnHMapperServer.Core.Extensions;
using HnHMapperServer.Core.Constants;

namespace HnHMapperServer.Api.Endpoints;

/// <summary>
/// Road API endpoints
/// </summary>
public static class RoadEndpoints
{
    public static void MapRoadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/map/api/v1/roads")
            .RequireAuthorization("TenantMapAccess"); // Require Map permission for all road APIs

        // List roads for a map
        group.MapGet("", GetRoads);

        // Get a single road
        group.MapGet("{id:int}", GetRoadById);

        // Create a new road
        group.MapPost("", CreateRoad)
            .RequireAuthorization("TenantMarkersAccess"); // Require Markers permission to create roads

        // Update an existing road
        group.MapPut("{id:int}", UpdateRoad);

        // Delete a road
        group.MapDelete("{id:int}", DeleteRoad);
    }

    /// <summary>
    /// Get all roads for a specific map
    /// </summary>
    private static async Task<IResult> GetRoads(
        HttpContext context,
        [FromQuery] int? mapId,
        IRoadService roadService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        // mapId is required
        if (!mapId.HasValue)
        {
            return Results.BadRequest(new { error = "mapId query parameter is required" });
        }

        var username = context.User.Identity?.Name ?? string.Empty;
        var isAdmin = HasPermission(context.User, Permission.Writer);

        try
        {
            var roads = await roadService.GetByMapIdAsync(mapId.Value, username, isAdmin);
            return Results.Json(roads);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    /// Get a single road by ID
    /// </summary>
    private static async Task<IResult> GetRoadById(
        HttpContext context,
        int id,
        IRoadService roadService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        var username = context.User.Identity?.Name ?? string.Empty;
        var isAdmin = HasPermission(context.User, Permission.Writer);

        try
        {
            var road = await roadService.GetByIdAsync(id, username, isAdmin);
            if (road == null)
            {
                return Results.NotFound(new { error = $"Road with ID {id} not found" });
            }

            return Results.Json(road);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    /// Create a new road
    /// </summary>
    private static async Task<IResult> CreateRoad(
        HttpContext context,
        [FromBody] CreateRoadDto dto,
        IRoadService roadService,
        IUpdateNotificationService updateNotificationService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        var username = context.User.Identity?.Name ?? string.Empty;

        try
        {
            var created = await roadService.CreateAsync(dto, username);

            // Extract tenant ID from context for SSE event
            var tenantId = context.Items["TenantId"] as string ?? string.Empty;

            // Publish SSE event for real-time updates
            var eventDto = new RoadEventDto
            {
                Id = created.Id,
                MapId = created.MapId,
                Name = created.Name,
                Waypoints = created.Waypoints,
                CreatedBy = created.CreatedBy,
                CreatedAt = created.CreatedAt,
                Hidden = created.Hidden,
                TenantId = tenantId
            };

            updateNotificationService.NotifyRoadCreated(eventDto);

            return Results.Created($"/map/api/v1/roads/{created.Id}", created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    /// Update an existing road
    /// </summary>
    private static async Task<IResult> UpdateRoad(
        HttpContext context,
        int id,
        [FromBody] UpdateRoadDto dto,
        IRoadService roadService,
        IUpdateNotificationService updateNotificationService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        var username = context.User.Identity?.Name ?? string.Empty;
        var isAdmin = HasPermission(context.User, Permission.Writer);

        try
        {
            var updated = await roadService.UpdateAsync(id, dto, username, isAdmin);

            // Extract tenant ID from context for SSE event
            var tenantId = context.Items["TenantId"] as string ?? string.Empty;

            // Publish SSE event for real-time updates
            var eventDto = new RoadEventDto
            {
                Id = updated.Id,
                MapId = updated.MapId,
                Name = updated.Name,
                Waypoints = updated.Waypoints,
                CreatedBy = updated.CreatedBy,
                CreatedAt = updated.CreatedAt,
                Hidden = updated.Hidden,
                TenantId = tenantId
            };

            updateNotificationService.NotifyRoadUpdated(eventDto);

            return Results.Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.StatusCode(403); // Forbidden
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    /// Delete a road
    /// </summary>
    private static async Task<IResult> DeleteRoad(
        HttpContext context,
        int id,
        IRoadService roadService,
        IUpdateNotificationService updateNotificationService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        var username = context.User.Identity?.Name ?? string.Empty;
        var isAdmin = HasPermission(context.User, Permission.Writer);

        try
        {
            await roadService.DeleteAsync(id, username, isAdmin);

            // Extract tenant ID from context for SSE event
            var tenantId = context.Items["TenantId"] as string ?? string.Empty;

            // Publish SSE event for real-time updates
            var deleteEvent = new RoadDeleteEventDto
            {
                Id = id,
                TenantId = tenantId
            };
            updateNotificationService.NotifyRoadDeleted(deleteEvent);

            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.StatusCode(403); // Forbidden
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    /// Check if user has specific tenant permission
    /// </summary>
    private static bool HasPermission(ClaimsPrincipal user, Permission permission)
    {
        // SuperAdmin bypasses all permission checks
        if (user.IsInRole(AuthorizationConstants.Roles.SuperAdmin))
            return true;

        var permissionValue = permission.ToClaimValue();
        return user.Claims.Any(c =>
            c.Type == AuthorizationConstants.ClaimTypes.TenantPermission &&
            c.Value.Equals(permissionValue, StringComparison.OrdinalIgnoreCase));
    }
}
