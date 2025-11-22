using HnHMapperServer.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Implementation of map preview service.
/// Generates composite images from map tiles for Discord notifications.
/// </summary>
public class MapPreviewService : IMapPreviewService
{
    private readonly ApplicationDbContext _db;
    private readonly IPreviewUrlSigningService _signingService;
    private readonly ILogger<MapPreviewService> _logger;
    private readonly string _gridStorage;
    private readonly string _iconStorage;
    private const int PREVIEW_SIZE = 4; // 4x4 grid of tiles
    private const int TILE_SIZE = 100; // Each tile is 100x100px
    private const int PREVIEW_RETENTION_DAYS = 2; // Reduced from 7 for security
    private static readonly TimeSpan PREVIEW_URL_VALIDITY = TimeSpan.FromHours(48); // Discord caching window

    public MapPreviewService(
        ApplicationDbContext db,
        IPreviewUrlSigningService signingService,
        ILogger<MapPreviewService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _signingService = signingService;
        _logger = logger;

        // Get grid storage path from configuration
        var configuredGridStorage = configuration["GridStorage"];
        _gridStorage = configuredGridStorage;
        if (string.IsNullOrWhiteSpace(_gridStorage))
        {
            // Default to shared solution-level path
            _gridStorage = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "map"));
        }
        else if (!Path.IsPathRooted(_gridStorage))
        {
            // Resolve relative path
            _gridStorage = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", _gridStorage));
        }

        // Get icon storage path from configuration (for marker icon overlay on previews)
        var configuredIconStorage = configuration["IconStorage"];
        if (!string.IsNullOrWhiteSpace(configuredIconStorage))
        {
            _iconStorage = Path.IsPathRooted(configuredIconStorage)
                ? configuredIconStorage
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", configuredIconStorage));
        }
        else
        {
            // Default to Web project's wwwroot directory
            _iconStorage = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "HnHMapperServer.Web", "wwwroot"));
        }

        _logger.LogInformation("MapPreviewService initialized with GridStorage={GridStorage}, IconStorage={IconStorage}",
            _gridStorage, _iconStorage);
    }

    /// <summary>
    /// Generate a 400x400px map preview showing a 4x4 grid of tiles with marker indicator.
    /// </summary>
    public async Task<string> GenerateMarkerPreviewAsync(
        int mapId,
        int markerCoordX,
        int markerCoordY,
        int markerX,
        int markerY,
        string tenantId,
        string webhookUrl,
        string? iconPath = null)
    {
        try
        {
            // Create 400x400px canvas
            using var preview = new Image<Rgba32>(PREVIEW_SIZE * TILE_SIZE, PREVIEW_SIZE * TILE_SIZE);
            preview.Mutate(ctx => ctx.BackgroundColor(Color.Transparent));

            // Center the marker in the preview (marker is at index 2,2 in the 4x4 grid)
            var centerOffset = PREVIEW_SIZE / 2;
            var startCoordX = markerCoordX - centerOffset + 1; // Adjusted to center better
            var startCoordY = markerCoordY - centerOffset + 1;

            int loadedTiles = 0;

            // Load 4x4 grid of tiles
            for (int dx = 0; dx < PREVIEW_SIZE; dx++)
            {
                for (int dy = 0; dy < PREVIEW_SIZE; dy++)
                {
                    var tileCoordX = startCoordX + dx;
                    var tileCoordY = startCoordY + dy;
                    var coord = new Coord(tileCoordX, tileCoordY);

                    try
                    {
                        // Query tile directly from database with explicit tenant filtering
                        // IMPORTANT: IgnoreQueryFilters() bypasses global tenant filter (no HttpContext in background task)
                        var tile = await _db.Tiles
                            .IgnoreQueryFilters()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(t =>
                                t.MapId == mapId &&
                                t.CoordX == tileCoordX &&
                                t.CoordY == tileCoordY &&
                                t.Zoom == 0 &&
                                t.TenantId == tenantId);

                        if (tile != null && !string.IsNullOrEmpty(tile.File))
                        {
                            var tilePath = Path.Combine(_gridStorage, tile.File);

                            if (File.Exists(tilePath))
                            {
                                using var tileImg = await Image.LoadAsync<Rgba32>(tilePath);

                                // Place tile at correct position (no resizing needed)
                                var x = dx * TILE_SIZE;
                                var y = dy * TILE_SIZE;

                                preview.Mutate(ctx => ctx.DrawImage(tileImg, new Point(x, y), 1f));
                                loadedTiles++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to load tile at ({X},{Y}) for preview", tileCoordX, tileCoordY);
                        // Continue loading other tiles
                    }
                }
            }

            if (loadedTiles == 0)
            {
                _logger.LogWarning("Preview generation failed: no tiles loaded for map {MapId} at ({X},{Y})",
                    mapId, markerCoordX, markerCoordY);
                throw new InvalidOperationException("No tiles available for preview generation");
            }

            _logger.LogDebug("Loaded {Count}/{Total} tiles for preview", loadedTiles, PREVIEW_SIZE * PREVIEW_SIZE);

            // Calculate marker position on the preview
            // The marker is in grid at (markerCoordX, markerCoordY) with offset (markerX, markerY)
            // In the preview, that grid is at index (markerCoordX - startCoordX, markerCoordY - startCoordY)
            var gridIndexX = markerCoordX - startCoordX;
            var gridIndexY = markerCoordY - startCoordY;

            var pinX = gridIndexX * TILE_SIZE + markerX;
            var pinY = gridIndexY * TILE_SIZE + markerY;

            // Draw marker indicator at marker location
            if (!string.IsNullOrEmpty(iconPath))
            {
                // Draw actual marker icon if provided
                DrawMarkerIcon(preview, pinX, pinY, iconPath);
            }
            else
            {
                // Fall back to red crosshair pin
                DrawMarkerPin(preview, pinX, pinY);
            }

            // Save preview to tenant-isolated directory
            var previewDir = Path.Combine(_gridStorage, "previews", tenantId);
            Directory.CreateDirectory(previewDir);

            // Generate cryptographically random preview ID (prevents enumeration attacks)
            var previewId = $"{Guid.NewGuid():N}.png";  // 32 hex characters + .png
            var previewPath = Path.Combine(previewDir, previewId);

            await preview.SaveAsPngAsync(previewPath);

            var fileInfo = new FileInfo(previewPath);
            _logger.LogInformation(
                "Generated map preview {PreviewId} for tenant {TenantId} ({Size}KB, {TileCount} tiles)",
                previewId, tenantId, fileInfo.Length / 1024, loadedTiles);

            // Generate signed URL with 48-hour expiration
            var signedUrl = _signingService.GenerateSignedUrl(previewId, webhookUrl, PREVIEW_URL_VALIDITY);

            _logger.LogDebug("Generated signed preview URL (valid for 48 hours)");

            return signedUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate map preview for map {MapId} at ({X},{Y})",
                mapId, markerCoordX, markerCoordY);
            throw;
        }
    }

    /// <summary>
    /// Draw a red pin indicator at the marker's exact position.
    /// Uses simple filled rectangles to create a visible marker.
    /// </summary>
    private void DrawMarkerPin(Image<Rgba32> image, int x, int y)
    {
        // Draw a simple cross/plus marker at the exact position
        // This is more reliable than trying to draw circles without the Drawing package
        var markerSize = 10;
        var thickness = 2;

        // Clamp coordinates to image bounds
        var centerX = Math.Clamp(x, markerSize, image.Width - markerSize);
        var centerY = Math.Clamp(y, markerSize, image.Height - markerSize);

        // Draw vertical line
        for (int dy = -markerSize; dy <= markerSize; dy++)
        {
            for (int t = -thickness; t <= thickness; t++)
            {
                var px = centerX + t;
                var py = centerY + dy;
                if (px >= 0 && px < image.Width && py >= 0 && py < image.Height)
                {
                    image[px, py] = new Rgba32(255, 0, 0, 255); // Red
                }
            }
        }

        // Draw horizontal line
        for (int dx = -markerSize; dx <= markerSize; dx++)
        {
            for (int t = -thickness; t <= thickness; t++)
            {
                var px = centerX + dx;
                var py = centerY + t;
                if (px >= 0 && px < image.Width && py >= 0 && py < image.Height)
                {
                    image[px, py] = new Rgba32(255, 0, 0, 255); // Red
                }
            }
        }

        // Draw white outline for contrast
        var outlineSize = markerSize + 1;
        var outlineThickness = 1;

        // White vertical line outline
        for (int dy = -outlineSize; dy <= outlineSize; dy++)
        {
            for (int t = -(thickness + outlineThickness); t <= (thickness + outlineThickness); t++)
            {
                var px = centerX + t;
                var py = centerY + dy;
                if (px >= 0 && px < image.Width && py >= 0 && py < image.Height)
                {
                    // Only draw if not already red (outline only)
                    if (Math.Abs(t) > thickness || Math.Abs(dy) > markerSize)
                    {
                        image[px, py] = new Rgba32(255, 255, 255, 255); // White
                    }
                }
            }
        }

        // White horizontal line outline
        for (int dx = -outlineSize; dx <= outlineSize; dx++)
        {
            for (int t = -(thickness + outlineThickness); t <= (thickness + outlineThickness); t++)
            {
                var px = centerX + dx;
                var py = centerY + t;
                if (px >= 0 && px < image.Width && py >= 0 && py < image.Height)
                {
                    // Only draw if not already red (outline only)
                    if (Math.Abs(t) > thickness || Math.Abs(dx) > markerSize)
                    {
                        image[px, py] = new Rgba32(255, 255, 255, 255); // White
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draw the actual marker icon at the specified position.
    /// Loads the icon image file and composites it over the preview.
    /// </summary>
    private void DrawMarkerIcon(Image<Rgba32> image, int x, int y, string iconPath)
    {
        try
        {
            // Ensure icon path has .png extension
            var fullIconPath = iconPath.EndsWith(".png") ? iconPath : $"{iconPath}.png";

            // Construct full path to icon file in icon storage (wwwroot)
            var iconFilePath = Path.Combine(_iconStorage, fullIconPath);

            if (!File.Exists(iconFilePath))
            {
                _logger.LogWarning("Marker icon not found at {IconPath} (resolved from '{IconStorage}/{IconPath}'), falling back to crosshair",
                    iconFilePath, _iconStorage, fullIconPath);
                DrawMarkerPin(image, x, y);
                return;
            }

            _logger.LogDebug("Loading marker icon from {IconPath}", iconFilePath);

            // Load the icon image
            using var iconImg = Image.Load<Rgba32>(iconFilePath);

            // Resize icon to 32x32 for better visibility on the preview
            const int ICON_SIZE = 32;
            iconImg.Mutate(ctx => ctx.Resize(ICON_SIZE, ICON_SIZE));

            // Center the icon at the marker position
            var iconX = x - (ICON_SIZE / 2);
            var iconY = y - (ICON_SIZE / 2);

            // Clamp to image bounds
            iconX = Math.Clamp(iconX, 0, image.Width - ICON_SIZE);
            iconY = Math.Clamp(iconY, 0, image.Height - ICON_SIZE);

            // Draw white background circle for better visibility
            DrawIconBackground(image, iconX + (ICON_SIZE / 2), iconY + (ICON_SIZE / 2), ICON_SIZE / 2 + 2);

            // Composite icon onto preview
            image.Mutate(ctx => ctx.DrawImage(iconImg, new Point(iconX, iconY), 1f));

            _logger.LogDebug("Drew marker icon {IconPath} at ({X},{Y})", iconPath, x, y);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load marker icon {IconPath}, falling back to crosshair", iconPath);
            DrawMarkerPin(image, x, y);
        }
    }

    /// <summary>
    /// Draw a white circular background behind the icon for better visibility.
    /// </summary>
    private void DrawIconBackground(Image<Rgba32> image, int centerX, int centerY, int radius)
    {
        // Draw a filled white circle using simple pixel iteration
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                // Check if point is within circle
                if (dx * dx + dy * dy <= radius * radius)
                {
                    var px = centerX + dx;
                    var py = centerY + dy;

                    if (px >= 0 && px < image.Width && py >= 0 && py < image.Height)
                    {
                        image[px, py] = new Rgba32(255, 255, 255, 255); // White
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get the file path for a preview image with tenant validation.
    /// </summary>
    public async Task<string?> GetPreviewPathAsync(string previewId, string tenantId)
    {
        await Task.CompletedTask; // Async for future expansion

        // Validate preview ID format (prevent directory traversal)
        if (string.IsNullOrWhiteSpace(previewId) ||
            previewId.Contains("..") ||
            previewId.Contains("/") ||
            previewId.Contains("\\") ||
            !previewId.EndsWith(".png"))
        {
            _logger.LogWarning("Invalid preview ID format: {PreviewId}", previewId);
            return null;
        }

        var previewPath = Path.Combine(_gridStorage, "previews", tenantId, previewId);

        if (!File.Exists(previewPath))
        {
            _logger.LogDebug("Preview not found: {PreviewPath}", previewPath);
            return null;
        }

        return previewPath;
    }

    /// <summary>
    /// Delete preview images older than 2 days.
    /// </summary>
    public async Task<int> CleanupOldPreviewsAsync()
    {
        await Task.CompletedTask; // Async for consistency

        var previewsDir = Path.Combine(_gridStorage, "previews");

        if (!Directory.Exists(previewsDir))
        {
            _logger.LogDebug("Previews directory does not exist, skipping cleanup");
            return 0;
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-PREVIEW_RETENTION_DAYS);
        int deletedCount = 0;

        try
        {
            // Recursively find all PNG files in preview directories
            var previewFiles = Directory.GetFiles(previewsDir, "*.png", SearchOption.AllDirectories);

            foreach (var filePath in previewFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);

                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        File.Delete(filePath);
                        deletedCount++;
                        _logger.LogDebug("Deleted old preview: {File}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete preview file: {File}", filePath);
                }
            }

            // Clean up empty tenant directories
            var tenantDirs = Directory.GetDirectories(previewsDir);
            foreach (var tenantDir in tenantDirs)
            {
                if (!Directory.EnumerateFileSystemEntries(tenantDir).Any())
                {
                    try
                    {
                        Directory.Delete(tenantDir);
                        _logger.LogDebug("Deleted empty preview directory: {Dir}", tenantDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete empty directory: {Dir}", tenantDir);
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old preview images (older than {Days} days)",
                    deletedCount, PREVIEW_RETENTION_DAYS);
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during preview cleanup");
            return deletedCount;
        }
    }
}
