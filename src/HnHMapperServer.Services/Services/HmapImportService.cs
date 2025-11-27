using System.Diagnostics;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Services.Interfaces;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for importing .hmap files into the map database
/// </summary>
public class HmapImportService : IHmapImportService
{
    private readonly IGridRepository _gridRepository;
    private readonly IMapRepository _mapRepository;
    private readonly ITileService _tileService;
    private readonly ITileRepository _tileRepository;
    private readonly IStorageQuotaService _quotaService;
    private readonly IMapNameService _mapNameService;
    private readonly IMarkerService _markerService;
    private readonly ILogger<HmapImportService> _logger;
    private const int GRID_SIZE = 100; // 100x100 tiles per grid

    public HmapImportService(
        IGridRepository gridRepository,
        IMapRepository mapRepository,
        ITileService tileService,
        ITileRepository tileRepository,
        IStorageQuotaService quotaService,
        IMapNameService mapNameService,
        IMarkerService markerService,
        ILogger<HmapImportService> logger)
    {
        _gridRepository = gridRepository;
        _mapRepository = mapRepository;
        _tileService = tileService;
        _tileRepository = tileRepository;
        _quotaService = quotaService;
        _mapNameService = mapNameService;
        _markerService = markerService;
        _logger = logger;
    }

    public async Task<HmapImportResult> ImportAsync(
        Stream hmapStream,
        string tenantId,
        HmapImportMode mode,
        string gridStorage,
        IProgress<HmapImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new HmapImportResult();

        try
        {
            // Phase 1: Parse .hmap file
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new HmapImportProgress { Phase = "Parsing", CurrentItem = 0, TotalItems = 1 });

            var reader = new HmapReader();
            var hmapData = reader.Read(hmapStream);

            _logger.LogInformation("Parsed .hmap: {GridCount} grids, {SegmentCount} segments",
                hmapData.Grids.Count, hmapData.GetSegmentIds().Count());

            // Phase 2: Filter to only the 3 largest segments by grid count
            cancellationToken.ThrowIfCancellationRequested();
            var allSegments = hmapData.GetSegmentIds()
                .Select(id => new { Id = id, GridCount = hmapData.GetGridsForSegment(id).Count })
                .OrderByDescending(s => s.GridCount)
                .ToList();

            const int MAX_SEGMENTS = 3;
            var segments = allSegments.Take(MAX_SEGMENTS).Select(s => s.Id).ToList();
            var skippedSegments = allSegments.Skip(MAX_SEGMENTS).ToList();

            if (skippedSegments.Count > 0)
            {
                _logger.LogInformation(
                    "Skipping {SkippedCount} smaller segments (keeping top {MaxSegments} by grid count). Skipped: {SkippedDetails}",
                    skippedSegments.Count,
                    MAX_SEGMENTS,
                    string.Join(", ", skippedSegments.Select(s => $"{s.Id:X}({s.GridCount} grids)")));
            }

            // Get grids only for segments we're importing
            var gridsToImport = segments
                .SelectMany(id => hmapData.GetGridsForSegment(id))
                .ToList();

            _logger.LogInformation("Will import {GridCount} grids from {SegmentCount} segments",
                gridsToImport.Count, segments.Count);

            // Phase 3: Collect tile resources only for grids we're importing
            cancellationToken.ThrowIfCancellationRequested();
            var allResources = gridsToImport
                .SelectMany(g => g.Tilesets.Select(t => t.ResourceName))
                .Distinct()
                .ToList();

            progress?.Report(new HmapImportProgress
            {
                Phase = "Fetching tiles",
                CurrentItem = 0,
                TotalItems = allResources.Count
            });

            // Phase 4: Fetch tile resources from Haven server
            var tileCacheDir = Path.Combine(gridStorage, "hmap-tile-cache");
            using var tileResourceService = new TileResourceService(tileCacheDir);

            var fetchProgress = new Progress<(int current, int total, string name)>(p =>
            {
                progress?.Report(new HmapImportProgress
                {
                    Phase = "Fetching tiles",
                    CurrentItem = p.current,
                    TotalItems = p.total,
                    CurrentItemName = p.name
                });
            });

            await tileResourceService.PrefetchTilesAsync(allResources, fetchProgress);

            // Phase 5: Process each segment
            var segmentIndex = 0;

            foreach (var segmentId in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                segmentIndex++;
                var segmentGrids = hmapData.GetGridsForSegment(segmentId);

                progress?.Report(new HmapImportProgress
                {
                    Phase = "Importing segments",
                    CurrentItem = segmentIndex,
                    TotalItems = segments.Count,
                    CurrentItemName = $"Segment {segmentId:X} ({segmentGrids.Count} grids)"
                });

                var (mapId, isNewMap, gridsImported, gridsSkipped, createdGridIds) = await ImportSegmentAsync(
                    segmentId, segmentGrids, tenantId, mode, gridStorage, tileResourceService, cancellationToken);

                if (mapId > 0)
                {
                    result.AffectedMapIds.Add(mapId);
                    if (isNewMap)
                    {
                        result.CreatedMapIds.Add(mapId);
                    }
                    if (gridsImported > 0)
                        result.MapsCreated++;
                }

                result.CreatedGridIds.AddRange(createdGridIds);
                result.GridsImported += gridsImported;
                result.GridsSkipped += gridsSkipped;
                result.TilesRendered += gridsImported;
            }

            // Phase 6: Generate zoom levels for affected maps
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new HmapImportProgress
            {
                Phase = "Generating zoom levels",
                CurrentItem = 0,
                TotalItems = result.AffectedMapIds.Count
            });

            var zoomIndex = 0;
            foreach (var mapId in result.AffectedMapIds.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                zoomIndex++;
                progress?.Report(new HmapImportProgress
                {
                    Phase = "Generating zoom levels",
                    CurrentItem = zoomIndex,
                    TotalItems = result.AffectedMapIds.Count,
                    CurrentItemName = $"Map {mapId}"
                });

                await GenerateZoomLevelsForMapAsync(mapId, tenantId, gridStorage);
            }

            // Phase 7: Import markers
            if (hmapData.Markers.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new HmapImportProgress
                {
                    Phase = "Importing markers",
                    CurrentItem = 0,
                    TotalItems = hmapData.Markers.Count
                });

                var markerIndex = 0;
                foreach (var segmentId in segments)
                {
                    var segmentMarkers = hmapData.GetMarkersForSegment(segmentId);
                    var segmentGrids = hmapData.GetGridsForSegment(segmentId);

                    // Build lookup: (GridTileX, GridTileY) -> GridId
                    // Grid's TileX/TileY are the grid coordinates in world space
                    var gridLookup = segmentGrids.ToDictionary(
                        g => (g.TileX, g.TileY),
                        g => g.GridIdString
                    );

                    foreach (var marker in segmentMarkers)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        markerIndex++;
                        progress?.Report(new HmapImportProgress
                        {
                            Phase = "Importing markers",
                            CurrentItem = markerIndex,
                            TotalItems = hmapData.Markers.Count,
                            CurrentItemName = marker.Name
                        });

                        // Convert marker's absolute tile coords to grid coords
                        // Marker TileX/TileY are absolute tile coordinates in world
                        var markerGridX = marker.TileX / GRID_SIZE;
                        var markerGridY = marker.TileY / GRID_SIZE;

                        // Find the grid this marker belongs to
                        if (!gridLookup.TryGetValue((markerGridX, markerGridY), out var gridId))
                        {
                            result.MarkersSkipped++;
                            continue; // No grid for this marker, skip
                        }

                        // Extract position within the grid (0-99)
                        var posX = marker.TileX % GRID_SIZE;
                        var posY = marker.TileY % GRID_SIZE;

                        // Determine image/icon based on marker type
                        var image = marker switch
                        {
                            HmapSMarker sm => sm.ResourceName,
                            _ => "gfx/terobjs/mm/custom"
                        };

                        try
                        {
                            // Create marker with correct sub-grid position
                            var markerData = new List<(string GridId, int X, int Y, string Name, string Image)>
                            {
                                (gridId, posX, posY, marker.Name, image)
                            };

                            await _markerService.BulkUploadMarkersAsync(markerData);
                            result.MarkersImported++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to import marker '{MarkerName}' at ({X},{Y})", marker.Name, posX, posY);
                            result.MarkersSkipped++;
                        }
                    }
                }

                _logger.LogInformation("Markers: {Imported} imported, {Skipped} skipped",
                    result.MarkersImported, result.MarkersSkipped);
            }

            result.Success = true;
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            _logger.LogInformation(
                "Import completed: {MapsCreated} maps, {GridsImported} grids imported, {GridsSkipped} skipped, {MarkersImported} markers, {Duration}ms",
                result.MapsCreated, result.GridsImported, result.GridsSkipped, result.MarkersImported, result.Duration.TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Import canceled for tenant {TenantId}", tenantId);
            result.Success = false;
            result.ErrorMessage = "Import was canceled";
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            return result;
        }
    }

    public async Task CleanupFailedImportAsync(
        IEnumerable<int> mapIds,
        IEnumerable<string> gridIds,
        string tenantId,
        string gridStorage)
    {
        _logger.LogInformation("Cleaning up failed import for tenant {TenantId}: {MapCount} maps, {GridCount} grids",
            tenantId, mapIds.Count(), gridIds.Count());

        // Delete grids first (they may reference maps)
        foreach (var gridId in gridIds)
        {
            try
            {
                var grid = await _gridRepository.GetGridAsync(gridId);
                if (grid != null)
                {
                    await _gridRepository.DeleteGridAsync(gridId);
                    _logger.LogDebug("Deleted grid {GridId}", gridId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete grid {GridId} during cleanup", gridId);
            }
        }

        // Delete maps (only newly created ones)
        foreach (var mapId in mapIds)
        {
            try
            {
                // Delete map directory (includes all tile files)
                var mapDir = Path.Combine(gridStorage, "tenants", tenantId, mapId.ToString());
                long totalDeletedBytes = 0;

                if (Directory.Exists(mapDir))
                {
                    // Calculate total size for storage quota adjustment
                    foreach (var file in Directory.GetFiles(mapDir, "*.png", SearchOption.AllDirectories))
                    {
                        try
                        {
                            totalDeletedBytes += new FileInfo(file).Length;
                        }
                        catch
                        {
                            // Ignore file access errors
                        }
                    }

                    try
                    {
                        Directory.Delete(mapDir, recursive: true);
                        _logger.LogDebug("Deleted map directory {MapDir}", mapDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete map directory {MapDir}", mapDir);
                    }
                }

                // Decrement storage quota
                if (totalDeletedBytes > 0)
                {
                    var sizeMB = totalDeletedBytes / (1024.0 * 1024.0);
                    await _quotaService.IncrementStorageUsageAsync(tenantId, -sizeMB);
                }

                // Delete all tile records for this map
                await _tileRepository.DeleteTilesByMapAsync(mapId);

                // Delete map record
                await _mapRepository.DeleteMapAsync(mapId);
                _logger.LogDebug("Deleted map {MapId}", mapId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete map {MapId} during cleanup", mapId);
            }
        }

        _logger.LogInformation("Cleanup completed for tenant {TenantId}", tenantId);
    }

    private async Task<(int mapId, bool isNewMap, int gridsImported, int gridsSkipped, List<string> createdGridIds)> ImportSegmentAsync(
        long segmentId,
        List<HmapGridData> grids,
        string tenantId,
        HmapImportMode mode,
        string gridStorage,
        TileResourceService tileResourceService,
        CancellationToken cancellationToken)
    {
        int mapId = 0;
        bool isNewMap = false;
        int gridsImported = 0;
        int gridsSkipped = 0;
        var createdGridIds = new List<string>();

        if (mode == HmapImportMode.CreateNew)
        {
            // Always create new map
            mapId = await CreateNewMapAsync(tenantId);
            isNewMap = true;
            _logger.LogInformation("Created new map {MapId} for segment {SegmentId:X}", mapId, segmentId);

            // Import all grids
            foreach (var grid in grids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ImportGridAsync(grid, mapId, tenantId, gridStorage, tileResourceService);
                createdGridIds.Add(grid.GridIdString);
                gridsImported++;
            }
        }
        else // Merge mode
        {
            // Check if any grids already exist
            int? existingMapId = null;
            foreach (var grid in grids)
            {
                var existing = await _gridRepository.GetGridAsync(grid.GridIdString);
                if (existing != null)
                {
                    existingMapId = existing.Map;
                    break;
                }
            }

            if (existingMapId.HasValue)
            {
                mapId = existingMapId.Value;
                isNewMap = false;
                _logger.LogInformation("Merging segment {SegmentId:X} into existing map {MapId}", segmentId, mapId);
            }
            else
            {
                mapId = await CreateNewMapAsync(tenantId);
                isNewMap = true;
                _logger.LogInformation("Created new map {MapId} for segment {SegmentId:X} (no existing grids)", mapId, segmentId);
            }

            // Import grids that don't exist
            foreach (var grid in grids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = await _gridRepository.GetGridAsync(grid.GridIdString);
                if (existing != null)
                {
                    gridsSkipped++;
                    continue;
                }

                await ImportGridAsync(grid, mapId, tenantId, gridStorage, tileResourceService);
                createdGridIds.Add(grid.GridIdString);
                gridsImported++;
            }
        }

        return (mapId, isNewMap, gridsImported, gridsSkipped, createdGridIds);
    }

    private async Task<int> CreateNewMapAsync(string tenantId)
    {
        var mapName = await _mapNameService.GenerateUniqueIdentifierAsync(tenantId);

        var mapInfo = new MapInfo
        {
            Id = 0, // Let SQLite auto-generate
            Name = mapName,
            Hidden = false,
            Priority = 0,
            CreatedAt = DateTime.UtcNow,
            TenantId = tenantId
        };

        await _mapRepository.SaveMapAsync(mapInfo);
        return mapInfo.Id;
    }

    private async Task ImportGridAsync(
        HmapGridData grid,
        int mapId,
        string tenantId,
        string gridStorage,
        TileResourceService tileResourceService)
    {
        // Create grid data entry
        var gridData = new GridData
        {
            Id = grid.GridIdString,
            Map = mapId,
            Coord = new Coord(grid.TileX, grid.TileY),
            NextUpdate = DateTime.UtcNow.AddMinutes(-1), // Past so it's immediately requestable
            TenantId = tenantId
        };

        await _gridRepository.SaveGridAsync(gridData);

        // Render tile image
        var tileImage = await RenderGridTileAsync(grid, tileResourceService);

        // Save tile to disk
        var relativePath = Path.Combine("tenants", tenantId, mapId.ToString(), "0", $"{grid.TileX}_{grid.TileY}.png");
        var fullPath = Path.Combine(gridStorage, relativePath);

        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await tileImage.SaveAsPngAsync(fullPath);
        var fileSize = (int)new FileInfo(fullPath).Length;

        // Save tile record
        await _tileService.SaveTileAsync(mapId, gridData.Coord, 0, relativePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), tenantId, fileSize);

        // Update storage quota
        var sizeMB = fileSize / (1024.0 * 1024.0);
        await _quotaService.IncrementStorageUsageAsync(tenantId, sizeMB);

        tileImage.Dispose();
    }

    private async Task<Image<Rgba32>> RenderGridTileAsync(HmapGridData grid, TileResourceService tileResourceService)
    {
        var result = new Image<Rgba32>(GRID_SIZE, GRID_SIZE);

        // Load tile textures for this grid
        var tileTex = new Image<Rgba32>?[grid.Tilesets.Count];
        for (int i = 0; i < grid.Tilesets.Count; i++)
        {
            tileTex[i] = await tileResourceService.GetTileImageAsync(grid.Tilesets[i].ResourceName);
        }

        // ===== PASS 1: Base texture sampling =====
        for (int y = 0; y < GRID_SIZE; y++)
        {
            for (int x = 0; x < GRID_SIZE; x++)
            {
                var tileIndex = y * GRID_SIZE + x;
                if (grid.TileIndices == null || tileIndex >= grid.TileIndices.Length)
                {
                    result[x, y] = new Rgba32(128, 128, 128); // Gray for missing
                    continue;
                }

                var tsetIdx = grid.TileIndices[tileIndex];
                if (tsetIdx >= tileTex.Length || tileTex[tsetIdx] == null)
                {
                    result[x, y] = new Rgba32(128, 128, 128); // Gray for missing tile
                    continue;
                }

                var tex = tileTex[tsetIdx]!;
                // Sample from tile texture using floormod for proper wrapping
                var tx = ((x % tex.Width) + tex.Width) % tex.Width;
                var ty = ((y % tex.Height) + tex.Height) % tex.Height;
                result[x, y] = tex[tx, ty];
            }
        }

        // ===== PASS 2: Ridge/cliff shading =====
        // Check height differences and darken cliffs
        if (grid.ZMap != null && grid.TileIndices != null)
        {
            const float CLIFF_THRESHOLD = 2.0f;  // Height diff that triggers cliff detection
            const float EPSILON = 0.01f;

            for (int y = 1; y < GRID_SIZE - 1; y++)
            {
                for (int x = 1; x < GRID_SIZE - 1; x++)
                {
                    var idx = y * GRID_SIZE + x;

                    // Check height breaks with 4 cardinal neighbors
                    float z = grid.ZMap[idx];
                    bool broken = false;

                    // North
                    if (Math.Abs(z - grid.ZMap[(y - 1) * GRID_SIZE + x]) > CLIFF_THRESHOLD + EPSILON)
                        broken = true;
                    // South
                    if (!broken && Math.Abs(z - grid.ZMap[(y + 1) * GRID_SIZE + x]) > CLIFF_THRESHOLD + EPSILON)
                        broken = true;
                    // West
                    if (!broken && Math.Abs(z - grid.ZMap[y * GRID_SIZE + (x - 1)]) > CLIFF_THRESHOLD + EPSILON)
                        broken = true;
                    // East
                    if (!broken && Math.Abs(z - grid.ZMap[y * GRID_SIZE + (x + 1)]) > CLIFF_THRESHOLD + EPSILON)
                        broken = true;

                    if (broken)
                    {
                        // Darken 3x3 area around cliff
                        // Center pixel gets 100% black, neighbors get 10% darkening
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                var px = x + dx;
                                var py = y + dy;
                                var blend = (dx == 0 && dy == 0) ? 1.0f : 0.1f;
                                result[px, py] = BlendToBlack(result[px, py], blend);
                            }
                        }
                    }
                }
            }
        }

        // ===== PASS 3: Tile priority borders =====
        // Draw black borders where neighbor tiles have higher priority (tile ID)
        if (grid.TileIndices != null)
        {
            for (int y = 0; y < GRID_SIZE; y++)
            {
                for (int x = 0; x < GRID_SIZE; x++)
                {
                    var idx = y * GRID_SIZE + x;
                    var tileId = grid.TileIndices[idx];

                    // Check 4 neighbors for higher tile IDs
                    bool hasHigherNeighbor = false;

                    if (x > 0 && grid.TileIndices[idx - 1] > tileId) hasHigherNeighbor = true;
                    if (!hasHigherNeighbor && x < GRID_SIZE - 1 && grid.TileIndices[idx + 1] > tileId) hasHigherNeighbor = true;
                    if (!hasHigherNeighbor && y > 0 && grid.TileIndices[idx - GRID_SIZE] > tileId) hasHigherNeighbor = true;
                    if (!hasHigherNeighbor && y < GRID_SIZE - 1 && grid.TileIndices[idx + GRID_SIZE] > tileId) hasHigherNeighbor = true;

                    if (hasHigherNeighbor)
                        result[x, y] = new Rgba32(0, 0, 0, 255);  // Black border
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Blend a color toward black by the specified factor (0.0 = no change, 1.0 = pure black)
    /// </summary>
    private static Rgba32 BlendToBlack(Rgba32 color, float factor)
    {
        var f1 = (int)(factor * 255);
        var f2 = 255 - f1;
        return new Rgba32(
            (byte)((color.R * f2) / 255),
            (byte)((color.G * f2) / 255),
            (byte)((color.B * f2) / 255),
            color.A
        );
    }

    private async Task GenerateZoomLevelsForMapAsync(int mapId, string tenantId, string gridStorage)
    {
        // Get all zoom-0 tiles for this map
        var grids = await _gridRepository.GetGridsByMapAsync(mapId);
        if (grids.Count == 0)
            return;

        // Build set of coordinates that need zoom regeneration
        var coordsToProcess = new HashSet<(int zoom, Coord coord)>();

        foreach (var grid in grids)
        {
            var coord = grid.Coord;
            for (int zoom = 1; zoom <= 6; zoom++)
            {
                coord = coord.Parent();
                coordsToProcess.Add((zoom, coord));
            }
        }

        // Process zoom levels in order (1 depends on 0, 2 depends on 1, etc.)
        for (int zoom = 1; zoom <= 6; zoom++)
        {
            var zoomCoords = coordsToProcess.Where(c => c.zoom == zoom).Select(c => c.coord).Distinct();
            foreach (var coord in zoomCoords)
            {
                try
                {
                    await _tileService.UpdateZoomLevelAsync(mapId, coord, zoom, tenantId, gridStorage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate zoom {Zoom} for {Coord}", zoom, coord);
                }
            }
        }
    }
}
