using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Services.Interfaces;
using HnHMapperServer.Services.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using HnHMapperServer.Core.Enums;
using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace HnHMapperServer.Api.Endpoints;

/// <summary>
/// Client endpoints for game client communication
/// Matches Go implementation routes: /client/{token}/*
/// </summary>
public static partial class ClientEndpoints
{
    private const string VERSION = "4";

    public static void MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        // Phase 7: Apply per-tenant rate limiting to all client endpoints
        var group = app.MapGroup("/client/{token}")
            .RequireRateLimiting("PerTenant");

        group.MapPost("/checkVersion", CheckVersion).DisableAntiforgery();
        group.MapGet("/checkVersion", CheckVersion); // Support Ender client (uses GET)
        group.MapGet("/locate", Locate);
        group.MapPost("/gridUpdate", GridUpdate).DisableAntiforgery();

        // Phase 7: Additional stricter rate limit for tile uploads (20/min)
        group.MapPost("/gridUpload", GridUpload)
            .DisableAntiforgery()
            .RequireRateLimiting("TileUpload");

        group.MapPost("/positionUpdate", PositionUpdate).DisableAntiforgery();
        group.MapPost("/markerBulkUpload", MarkerBulkUpload).DisableAntiforgery();
        group.MapPost("/markerDelete", MarkerDelete).DisableAntiforgery();
        group.MapPost("/markerUpdate", MarkerUpdate).DisableAntiforgery();
        group.MapPost("/markerReadyTime", MarkerReadyTime).DisableAntiforgery();

        // Cookbook endpoints
        group.MapPost("/foodSubmit", FoodSubmit).DisableAntiforgery();

        group.MapGet("", RedirectToMap);
    }

    private static IResult RedirectToMap()
    {
        return Results.Redirect("/map/");
    }

    private static IResult CheckVersion([FromRoute] string token, [FromQuery] string version)
    {
        return version == VERSION ? Results.Ok() : Results.BadRequest();
    }

    private static async Task<IResult> Locate(
        [FromRoute] string token,
        [FromQuery] string gridID,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IGridService gridService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        var location = await gridService.LocateGridAsync(gridID);
        if (location == null)
            return Results.NotFound();

        var (mapId, coord) = location.Value;
        return Results.Text($"{mapId};{coord.X};{coord.Y}");
    }

    private static async Task<IResult> GridUpdate(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IGridService gridService,
        IConfiguration configuration,
        HnHMapperServer.Api.Services.MapRevisionCache revisionCache,
        IUpdateNotificationService updateNotificationService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read raw JSON and deserialize with case-insensitive options (Go client sends lowercase)
        var gridUpdate = await context.Request.ReadFromJsonAsync<GridUpdateDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (gridUpdate == null)
            return Results.BadRequest("Invalid grid update payload");

        var gridStorage = configuration["GridStorage"] ?? "map";
        var response = await gridService.ProcessGridUpdateAsync(gridUpdate, gridStorage);

        // Bump map revision and notify clients to invalidate cache
        var newRevision = revisionCache.Increment(response.Map);
        updateNotificationService.NotifyMapRevision(response.Map, newRevision);
        logger.LogDebug("GridUpdate: Bumped revision for map {MapId} to {Revision}", response.Map, newRevision);

        return Results.Json(response);
    }

    private static async Task<IResult> GridUpload(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IGridRepository gridRepository,
        ITileService tileService,
        TileCacheService tileCacheService,
        IConfiguration configuration,
        HnHMapperServer.Api.Services.MapRevisionCache revisionCache,
        IUpdateNotificationService updateNotificationService,
        IStorageQuotaService quotaService,
        ITenantFilePathService filePathService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Get tenant ID from context (set by token validation)
        var tenantId = context.Items["TenantId"] as string;
        if (string.IsNullOrEmpty(tenantId))
        {
            logger.LogError("GridUpload: TenantId not found in context");
            return Results.Unauthorized();
        }

        var request = context.Request;

        if (!request.HasFormContentType)
            return Results.BadRequest("Expected multipart/form-data");

        var form = await request.ReadFormAsync();
        var id = form["id"].ToString();
        var extraData = form["extraData"].ToString();

        // Check for winter season skip logic
        if (!string.IsNullOrEmpty(extraData))
        {
            try
            {
                var ed = JsonSerializer.Deserialize<Dictionary<string, object>>(extraData);
                // Case-insensitive lookup for "season" field (Ender client sends lowercase)
                object? season = null;
                if (ed != null)
                {
                    foreach (var kvp in ed)
                    {
                        if (kvp.Key.Equals("Season", StringComparison.OrdinalIgnoreCase))
                        {
                            season = kvp.Value;
                            break;
                        }
                    }
                }

                if (season != null)
                {
                    var seasonInt = Convert.ToInt32(season.ToString());
                    if (seasonInt == 3)
                    {
                        var winterGrid = await gridRepository.GetGridAsync(id);
                        if (winterGrid != null)
                        {
                            var tile = await tileService.GetTileAsync(winterGrid.Map, winterGrid.Coord, 0);
                            if (tile != null && !string.IsNullOrEmpty(tile.File))
                            {
                                // Already have tile, skip winter upload
                                if (DateTime.UtcNow < winterGrid.NextUpdate)
                                {
                                    logger.LogInformation("Ignoring tile upload: winter");
                                    return Results.Ok();
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        var file = form.Files["file"];
        if (file == null)
            return Results.BadRequest("No file provided");

        var grid = await gridRepository.GetGridAsync(id);
        if (grid == null)
        {
            // Grid doesn't exist for current tenant
            return Results.BadRequest($"Unknown grid id: {id}");
        }

        var gridStorage = configuration["GridStorage"] ?? "map";
        var updateTile = DateTime.UtcNow > grid.NextUpdate;

        if (updateTile)
        {
            // Check storage quota before accepting upload
            var fileSizeMB = file.Length / 1024.0 / 1024.0;
            var canUpload = await quotaService.CheckQuotaAsync(tenantId, fileSizeMB);

            if (!canUpload)
            {
                var currentUsage = await quotaService.GetCurrentUsageAsync(tenantId);
                var quotaLimit = await quotaService.GetQuotaLimitAsync(tenantId);

                logger.LogWarning(
                    "GridUpload: Quota exceeded for tenant {TenantId}. Current: {Current}MB, Quota: {Quota}MB, Upload: {Upload}MB",
                    tenantId, currentUsage, quotaLimit, fileSizeMB);

                return Results.Json(new
                {
                    error = "Storage quota exceeded",
                    detail = $"Your tenant has reached its storage limit. Current usage: {currentUsage:F2} MB / {quotaLimit} MB. Attempted upload: {fileSizeMB:F2} MB."
                }, statusCode: 413);
            }

            grid.NextUpdate = DateTime.UtcNow.AddMinutes(30);
            await gridRepository.SaveGridAsync(grid);

            // Ensure tenant directories exist
            filePathService.EnsureTenantDirectoriesExist(tenantId, gridStorage);

            // Use tenant-specific file path
            var filePath = filePathService.GetGridFilePath(tenantId, grid.Id, gridStorage);
            var fileDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            // Save file and increment quota atomically
            using (var transaction = await db.Database.BeginTransactionAsync())
            {
                try
                {
                    // Save file to disk
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Increment storage usage
                    await quotaService.IncrementStorageUsageAsync(tenantId, fileSizeMB);

                    await transaction.CommitAsync();

                    logger.LogDebug(
                        "GridUpload: Saved tile for tenant {TenantId}, grid {GridId}, size {SizeMB:F2}MB",
                        tenantId, grid.Id, fileSizeMB);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    logger.LogError(ex, "GridUpload: Failed to save tile for tenant {TenantId}, grid {GridId}", tenantId, grid.Id);

                    // Clean up file if it was created
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch { }
                    }

                    throw;
                }
            }

            var relativePath = filePathService.GetGridRelativePath(tenantId, grid.Id);
            var fileSizeBytes = (int)new FileInfo(filePath).Length;
            await tileService.SaveTileAsync(
                grid.Map,
                grid.Coord,
                0,
                relativePath,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                tenantId,
                fileSizeBytes);

            // Update zoom levels
            var c = grid.Coord;
            for (int z = 1; z <= 6; z++)
            {
                c = c.Parent();
                await tileService.UpdateZoomLevelAsync(grid.Map, c, z, tenantId, gridStorage);
            }

            // Bump map revision and notify clients to invalidate cache
            var newRevision = revisionCache.Increment(grid.Map);
            updateNotificationService.NotifyMapRevision(grid.Map, newRevision);
            logger.LogDebug("GridUpload: Bumped revision for map {MapId} to {Revision}", grid.Map, newRevision);

            // Invalidate tile cache for current tenant (tiles were modified - SSE clients need fresh data)
            await tileCacheService.InvalidateCacheAsync(tenantId);
        }

        return Results.Ok();
    }

    private static async Task<IResult> PositionUpdate(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IGridRepository gridRepository,
        ICharacterService characterService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read raw JSON with case-insensitive options (Go client sends lowercase: coords, gridID, name, etc.)
        var positions = await context.Request.ReadFromJsonAsync<Dictionary<string, Dictionary<string, object>>>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (positions == null)
            return Results.BadRequest("Invalid position update payload");

        // Diagnostics: track processing stats
        int total = 0, processed = 0, skippedMissingGrid = 0, skippedMissingCoords = 0;
        var unknownGrids = new HashSet<string>();
        int loggedMissingCoords = 0;
        int loggedMissingGrid = 0;

        foreach (var (id, data) in positions)
        {
            total++;

            // Helper: case-insensitive key lookup in dictionary
            object? GetValue(string key)
            {
                foreach (var kvp in data)
                {
                    if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;
                }
                return null;
            }

            // Safely get values from dictionary, handling missing keys (case-insensitive)
            var name = GetValue("Name")?.ToString() ?? "";
            var gridId = GetValue("GridID")?.ToString() ?? "";
            var coordsObj = GetValue("Coords");
            var coords = coordsObj as JsonElement?;
            var type = GetValue("Type")?.ToString() ?? "";
            var rotation = Convert.ToInt32(GetValue("Rotation")?.ToString() ?? "0");
            var speed = Convert.ToInt32(GetValue("Speed")?.ToString() ?? "0");

            if (coords == null)
            {
                skippedMissingCoords++;
                if (loggedMissingCoords < 5)
                {
                    // Log available keys to help diagnose payload structure
                    var availableKeys = string.Join(", ", data.Keys);
                    logger.LogWarning("PositionUpdate: Skipping character ID={CharacterId} Name={Name} GridID={GridId} - missing Coords field. Available keys in payload: [{Keys}]. Expected: Coords={{X,Y}}", id, name, gridId, availableKeys);
                    loggedMissingCoords++;
                }
                continue;
            }

            // Extract X and Y from coords (case-insensitive: try "X" then "x", "Y" then "y")
            int x = 0, y = 0;
            try
            {
                if (coords.Value.TryGetProperty("X", out var xProp))
                    x = xProp.GetInt32();
                else if (coords.Value.TryGetProperty("x", out var xPropLower))
                    x = xPropLower.GetInt32();

                if (coords.Value.TryGetProperty("Y", out var yProp))
                    y = yProp.GetInt32();
                else if (coords.Value.TryGetProperty("y", out var yPropLower))
                    y = yPropLower.GetInt32();
            }
            catch (Exception ex)
            {
                skippedMissingCoords++;
                if (loggedMissingCoords < 5)
                {
                    logger.LogWarning(ex, "PositionUpdate: Failed to parse coords X/Y for character ID={CharacterId} Name={Name}", id, name);
                    loggedMissingCoords++;
                }
                continue;
            }

            var grid = await gridRepository.GetGridAsync(gridId);
            if (grid == null)
            {
                skippedMissingGrid++;
                unknownGrids.Add(gridId);
                if (loggedMissingGrid < 5)
                {
                    logger.LogWarning("PositionUpdate: Skipping character ID={CharacterId} Name={Name} - unknown GridID={GridId}. Client must send gridUpdate first to register this grid.", id, name, gridId);
                    loggedMissingGrid++;
                }
                continue;
            }

            // Extract tenant ID from context (set by TenantContextMiddleware via token validation)
            var tenantId = context.Items["TenantId"] as string ?? string.Empty;

            var character = new Character
            {
                Name = name,
                Id = int.Parse(id),
                Map = grid.Map,
                Position = new Position(
                    x + (grid.Coord.X * 100),
                    y + (grid.Coord.Y * 100)),
                Type = type,
                Rotation = rotation,
                Speed = speed,
                Updated = DateTime.UtcNow,
                TenantId = tenantId
            };

            characterService.UpdateCharacter(id, character);
            processed++;
        }

        // Log summary for every positionUpdate
        if (total > 0)
        {
            logger.LogDebug("PositionUpdate: total={Total}, processed={Processed}, skippedMissingGrid={SkippedMissingGrid}, skippedMissingCoords={SkippedMissingCoords} | unknownGridsSample={Sample}",
                total, processed, skippedMissingGrid, skippedMissingCoords, string.Join(",", unknownGrids.Take(3)));
        }

        return Results.Ok();
    }

    private static async Task<IResult> MarkerBulkUpload(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IMarkerService markerService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read raw JSON with case-insensitive options (Go client sends lowercase keys)
        var markers = await context.Request.ReadFromJsonAsync<List<Dictionary<string, object>>>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (markers == null)
            return Results.BadRequest("Invalid marker bulk upload payload");

        // Helper: case-insensitive key lookup in dictionary
        object? GetValue(Dictionary<string, object> dict, string key)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return null;
        }

        var markerList = markers.Select(m => (
            GridId: GetValue(m, "GridID")?.ToString() ?? "",
            X: Convert.ToInt32(GetValue(m, "X")?.ToString() ?? "0"),
            Y: Convert.ToInt32(GetValue(m, "Y")?.ToString() ?? "0"),
            Name: GetValue(m, "Name")?.ToString() ?? "",
            Image: GetValue(m, "Image")?.ToString() ?? ""
        )).ToList();

        await markerService.BulkUploadMarkersAsync(markerList);
        return Results.Ok();
    }

    private static async Task<IResult> MarkerDelete(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IMarkerService markerService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read raw JSON with case-insensitive options (Go client sends lowercase keys)
        var markers = await context.Request.ReadFromJsonAsync<List<Dictionary<string, object>>>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (markers == null)
            return Results.BadRequest("Invalid marker delete payload");

        // Helper: case-insensitive key lookup in dictionary
        object? GetValue(Dictionary<string, object> dict, string key)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return null;
        }

        var markerList = markers.Select(m => (
            GridId: GetValue(m, "GridID")?.ToString() ?? "",
            X: Convert.ToInt32(GetValue(m, "X")?.ToString() ?? "0"),
            Y: Convert.ToInt32(GetValue(m, "Y")?.ToString() ?? "0")
        )).ToList();

        await markerService.DeleteMarkersAsync(markerList);
        return Results.Ok();
    }

    private static async Task<IResult> MarkerUpdate(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IMarkerService markerService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read raw JSON with case-insensitive options (Go client sends lowercase keys)
        var markers = await context.Request.ReadFromJsonAsync<List<Dictionary<string, object>>>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (markers == null)
            return Results.BadRequest("Invalid marker update payload");

        // Helper: case-insensitive key lookup in dictionary
        object? GetValue(Dictionary<string, object> dict, string key)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return null;
        }

        logger.LogWarning("MarkerUpdate: Received {Count} markers", markers.Count);

        foreach (var m in markers)
        {
            // Safely get values from dictionary, handling missing keys (case-insensitive)
            var gridId = GetValue(m, "GridID")?.ToString() ?? "";
            var x = Convert.ToInt32(GetValue(m, "X")?.ToString() ?? "0");
            var y = Convert.ToInt32(GetValue(m, "Y")?.ToString() ?? "0");
            var name = GetValue(m, "Name")?.ToString() ?? "";
            var image = GetValue(m, "Image")?.ToString() ?? "";
            var ready = Convert.ToBoolean(GetValue(m, "Ready")?.ToString() ?? "false");

            logger.LogWarning(
                "MarkerUpdate: Processing marker - GridID={GridId}, X={X}, Y={Y}, Name={Name}, Image={Image}, Ready={Ready}",
                gridId, x, y, name, image, ready);

            await markerService.UpdateMarkerAsync(gridId, x, y, name, image, ready);
        }

        return Results.Ok();
    }

    private static async Task<IResult> MarkerReadyTime(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IMarkerService markerService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read raw JSON with case-insensitive options (Go client sends lowercase keys)
        var markers = await context.Request.ReadFromJsonAsync<List<Dictionary<string, object>>>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (markers == null)
            return Results.BadRequest("Invalid marker ready time payload");

        // Helper: case-insensitive key lookup in dictionary
        object? GetValue(Dictionary<string, object> dict, string key)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return null;
        }

        foreach (var m in markers)
        {
            // Safely get values from dictionary, handling missing keys (case-insensitive)
            var gridId = GetValue(m, "GridID")?.ToString() ?? "";
            var x = Convert.ToInt32(GetValue(m, "X")?.ToString() ?? "0");
            var y = Convert.ToInt32(GetValue(m, "Y")?.ToString() ?? "0");
            var maxReady = Convert.ToInt64(GetValue(m, "MaxReady")?.ToString() ?? "-1");
            var minReady = Convert.ToInt64(GetValue(m, "MinReady")?.ToString() ?? "-1");

            await markerService.UpdateMarkerReadyTimeAsync(gridId, x, y, maxReady, minReady);
        }

        return Results.Ok();
    }

    private static class ClientTokenHelpers
    {
        /// <summary>
        /// Validates token using TokenService, extracts tenant information, and stores tenant context.
        /// Returns true if token is valid for upload operations, false otherwise.
        ///
        /// Token format: {tenantId}_{secret} (e.g., "warrior-shield-42_abc123...")
        /// </summary>
        public static async Task<bool> HasUploadAsync(
            HttpContext httpContext,
            ApplicationDbContext db,
            ITokenService tokenService,
            string fullToken,
            ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(fullToken))
            {
                logger?.LogWarning("Token validation failed: empty or null token provided");
                return false;
            }

            // Use TokenService to validate token and extract tenant
            var validationResult = await tokenService.ValidateTokenAsync(fullToken);

            if (!validationResult.IsValid)
            {
                logger?.LogWarning(
                    "Token validation failed: {ErrorMessage}",
                    validationResult.ErrorMessage ?? "Unknown error");
                return false;
            }

            // Store tenant ID and user ID in HttpContext.Items for ITenantContextAccessor
            httpContext.Items["TenantId"] = validationResult.TenantId;
            httpContext.Items["UserId"] = validationResult.UserId;

            logger?.LogDebug(
                "Token validation successful. TenantId={TenantId}, UserId={UserId}",
                validationResult.TenantId, validationResult.UserId);

            return true;
        }
    }

    /// <summary>
    /// Endpoint: POST /client/{token}/foodSubmit
    /// Submit a food/recipe to the cookbook
    /// </summary>
    private static async Task<IResult> FoodSubmit(
        [FromRoute] string token,
        HttpContext context,
        ApplicationDbContext db,
        ITokenService tokenService,
        IFoodSubmissionService foodSubmissionService,
        ILogger<Program> logger)
    {
        if (!await ClientTokenHelpers.HasUploadAsync(context, db, tokenService, token, logger))
            return Results.Unauthorized();

        // Read food data from request body
        var foodDto = await context.Request.ReadFromJsonAsync<SubmitFoodDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (foodDto == null)
            return Results.BadRequest("Invalid food submission payload");

        // Get user ID from context
        var userId = context.Items["UserId"] as string;
        if (userId == null)
            return Results.Unauthorized();

        try
        {
            // Submit food (auto-publishes if verified, creates submission otherwise)
            var (published, foodId, submissionId) = await foodSubmissionService.SubmitFoodAsync(foodDto, userId);

            if (published)
            {
                logger.LogInformation("Food auto-published: Id={FoodId}, UserId={UserId}", foodId, userId);
                return Results.Ok(new { status = "published", foodId });
            }
            else
            {
                logger.LogInformation("Food submission created: Id={SubmissionId}, UserId={UserId}", submissionId, userId);
                return Results.Ok(new { status = "pending_approval", submissionId });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to submit food: UserId={UserId}", userId);
            return Results.Problem("Failed to submit food");
        }
    }
}

