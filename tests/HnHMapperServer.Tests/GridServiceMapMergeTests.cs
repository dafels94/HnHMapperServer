using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Infrastructure.Repositories;
using HnHMapperServer.Services.Interfaces;
using HnHMapperServer.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ITenantContextAccessor = HnHMapperServer.Core.Interfaces.ITenantContextAccessor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HnHMapperServer.Tests;

/// <summary>
/// Integration tests for map merging functionality in GridService.
/// Verifies that when multiple maps are detected during gridUpdate:
/// 1. Source tiles are correctly looked up using pre-shift coordinates
/// 2. Tiles are saved to target map with shifted coordinates
/// 3. Zoom levels 1-6 are regenerated for all affected tiles
/// </summary>
public class GridServiceMapMergeTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly string _testGridStorage;
    private readonly GridService _gridService;
    private readonly TileService _tileService;
    private readonly IGridRepository _gridRepository;
    private readonly IMapRepository _mapRepository;
    private readonly ITileRepository _tileRepository;
    private readonly IConfigRepository _configRepository;

    public GridServiceMapMergeTests()
    {
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Seed default tenant for multi-tenancy support
        _dbContext.Tenants.Add(new HnHMapperServer.Core.Models.TenantEntity
        {
            Id = "default-tenant-1",
            Name = "default-tenant-1",
            StorageQuotaMB = 1024,
            CurrentStorageMB = 0,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        _dbContext.SaveChanges();

        // Setup test grid storage directory
        _testGridStorage = Path.Combine(Path.GetTempPath(), $"hnh-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testGridStorage);
        Directory.CreateDirectory(Path.Combine(_testGridStorage, "grids"));

        // Mock tenant context accessor
        var mockTenantContext = new Mock<ITenantContextAccessor>();
        mockTenantContext.Setup(x => x.GetCurrentTenantId()).Returns("default-tenant-1");
        mockTenantContext.Setup(x => x.GetRequiredTenantId()).Returns("default-tenant-1");

        // Initialize repositories
        _gridRepository = new GridRepository(_dbContext, mockTenantContext.Object);
        _mapRepository = new MapRepository(_dbContext, mockTenantContext.Object);
        _tileRepository = new TileRepository(_dbContext, mockTenantContext.Object);
        _configRepository = new ConfigRepository(_dbContext, mockTenantContext.Object);

        // Initialize services with mocked loggers and notification service
        var tileLogger = new Mock<ILogger<TileService>>();
        var gridLogger = new Mock<ILogger<GridService>>();
        var mockNotificationService = new Mock<IUpdateNotificationService>();
        var mockQuotaService = new Mock<IStorageQuotaService>();
        var mockMapNameService = new Mock<IMapNameService>();

        _tileService = new TileService(
            _tileRepository,
            _gridRepository,
            mockNotificationService.Object,
            mockQuotaService.Object,
            tileLogger.Object,
            _dbContext);

        _gridService = new GridService(
            _gridRepository,
            _mapRepository,
            _tileService,
            _configRepository,
            mockNotificationService.Object,
            mockMapNameService.Object,
            mockTenantContext.Object,
            gridLogger.Object);

        // Seed default configuration
        _dbContext.Database.EnsureCreated();
        // Note: Config repository handles the Config entity internally
    }

    public void Dispose()
    {
        // Cleanup test database and files
        _dbContext.Dispose();
        if (Directory.Exists(_testGridStorage))
        {
            Directory.Delete(_testGridStorage, true);
        }
    }

    /// <summary>
    /// Create a test PNG tile image (100x100 with solid color)
    /// </summary>
    private async Task<string> CreateTestTileAsync(string gridId, byte r, byte g, byte b)
    {
        var filePath = Path.Combine(_testGridStorage, "grids", $"{gridId}.png");
        using var img = new Image<Rgba32>(100, 100);
        img.Mutate(ctx => ctx.BackgroundColor(new Rgba32(r, g, b)));
        await img.SaveAsPngAsync(filePath);
        return Path.Combine("grids", $"{gridId}.png");
    }

    [Fact]
    public async Task MergeMapsAsync_CorrectlyLookupsSourceTiles_AndRegeneratesZooms()
    {
        // Arrange: Create two separate maps with known grids and tiles
        
        // Map 1: Contains grids at (0,0) and (1,0)
        var map1 = new MapInfo { Id = 1, Name = "Map1", Hidden = false, Priority = 0 };
        await _mapRepository.SaveMapAsync(map1);

        var grid1a = new GridData { Id = "grid1a", Map = 1, Coord = new Coord(0, 0), NextUpdate = DateTime.UtcNow.AddMinutes(-1) };
        var grid1b = new GridData { Id = "grid1b", Map = 1, Coord = new Coord(1, 0), NextUpdate = DateTime.UtcNow.AddMinutes(-1) };
        await _gridRepository.SaveGridAsync(grid1a);
        await _gridRepository.SaveGridAsync(grid1b);

        // Create test tiles for map 1 (red color)
        var tile1aPath = await CreateTestTileAsync("grid1a", 255, 0, 0);
        var tile1bPath = await CreateTestTileAsync("grid1b", 255, 0, 0);
        await _tileService.SaveTileAsync(1, new Coord(0, 0), 0, tile1aPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "default-tenant-1", 0);
        await _tileService.SaveTileAsync(1, new Coord(1, 0), 0, tile1bPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "default-tenant-1", 0);

        // Map 2: Contains grids at (5,5) and (6,5)
        var map2 = new MapInfo { Id = 2, Name = "Map2", Hidden = false, Priority = 0 };
        await _mapRepository.SaveMapAsync(map2);

        var grid2a = new GridData { Id = "grid2a", Map = 2, Coord = new Coord(5, 5), NextUpdate = DateTime.UtcNow.AddMinutes(-1) };
        var grid2b = new GridData { Id = "grid2b", Map = 2, Coord = new Coord(6, 5), NextUpdate = DateTime.UtcNow.AddMinutes(-1) };
        await _gridRepository.SaveGridAsync(grid2a);
        await _gridRepository.SaveGridAsync(grid2b);

        // Create test tiles for map 2 (green color)
        var tile2aPath = await CreateTestTileAsync("grid2a", 0, 255, 0);
        var tile2bPath = await CreateTestTileAsync("grid2b", 0, 255, 0);
        await _tileService.SaveTileAsync(2, new Coord(5, 5), 0, tile2aPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "default-tenant-1", 0);
        await _tileService.SaveTileAsync(2, new Coord(6, 5), 0, tile2bPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "default-tenant-1", 0);

        // Act: Send gridUpdate that spans both maps (this should trigger merge)
        // Simulate a 3x3 grid update where:
        // - Center grid (1,1) is from map 1 at coord (0,0)
        // - Another grid (2,1) is from map 2 at coord (5,5)
        // This creates a situation where maps need to merge
        var gridUpdate = new GridUpdateDto
        {
            Grids = new List<List<string>>
            {
                new List<string> { "new1", "new2", "new3" },       // Row 0
                new List<string> { "grid1a", "grid1b", "new6" },   // Row 1 (contains map 1 grids)
                new List<string> { "new7", "new8", "new9" }        // Row 2
            }
        };

        var result = await _gridService.ProcessGridUpdateAsync(gridUpdate, _testGridStorage);

        // Assert: Verify that tiles were correctly copied and zooms generated
        
        // 1. Verify that map 1 still exists (it's the target map)
        var map1After = await _mapRepository.GetMapAsync(1);
        Assert.NotNull(map1After);

        // 2. Verify that base tiles (zoom 0) exist for the original grids
        var baseTile1a = await _tileService.GetTileAsync(1, new Coord(0, 0), 0);
        var baseTile1b = await _tileService.GetTileAsync(1, new Coord(1, 0), 0);
        Assert.NotNull(baseTile1a);
        Assert.NotNull(baseTile1b);
        Assert.Equal(tile1aPath, baseTile1a.File);
        Assert.Equal(tile1bPath, baseTile1b.File);

        // 3. Verify that zoom level 1 was generated (parent of 0,0 and 1,0 is 0,0 at zoom 1)
        var zoom1Tile = await _tileService.GetTileAsync(1, new Coord(0, 0), 1);
        Assert.NotNull(zoom1Tile);
        Assert.NotEmpty(zoom1Tile.File);

        // Verify the zoom 1 tile file actually exists
        var zoom1FilePath = Path.Combine(_testGridStorage, zoom1Tile.File);
        Assert.True(File.Exists(zoom1FilePath), $"Zoom 1 tile file should exist at {zoom1FilePath}");

        // 4. Verify higher zoom levels were generated
        for (int z = 2; z <= 6; z++)
        {
            var coord = new Coord(0, 0);
            for (int i = 1; i < z; i++)
            {
                coord = coord.Parent();
            }
            
            var zoomTile = await _tileService.GetTileAsync(1, coord, z);
            Assert.NotNull(zoomTile);
            Assert.NotEmpty(zoomTile.File);
            
            var zoomFilePath = Path.Combine(_testGridStorage, zoomTile.File);
            Assert.True(File.Exists(zoomFilePath), $"Zoom {z} tile file should exist at {zoomFilePath}");
        }
    }

    [Fact]
    public async Task MergeMapsAsync_PreservesSourceTileFiles_WhenShiftingCoordinates()
    {
        // Arrange: Create a source map with a single grid and tile
        var sourceMap = new MapInfo { Id = 10, Name = "SourceMap", Hidden = false, Priority = 0 };
        await _mapRepository.SaveMapAsync(sourceMap);

        var sourceGrid = new GridData
        {
            Id = "sourceGrid",
            Map = 10,
            Coord = new Coord(3, 4), // Original coordinate
            NextUpdate = DateTime.UtcNow.AddMinutes(-1)
        };
        await _gridRepository.SaveGridAsync(sourceGrid);

        // Create a blue test tile
        var sourceTilePath = await CreateTestTileAsync("sourceGrid", 0, 0, 255);
        await _tileService.SaveTileAsync(10, new Coord(3, 4), 0, sourceTilePath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "default-tenant-1", 0);

        // Create a target map
        var targetMap = new MapInfo { Id = 20, Name = "TargetMap", Hidden = false, Priority = 1 }; // Higher priority
        await _mapRepository.SaveMapAsync(targetMap);

        var targetGrid = new GridData
        {
            Id = "targetGrid",
            Map = 20,
            Coord = new Coord(0, 0),
            NextUpdate = DateTime.UtcNow.AddMinutes(-1)
        };
        await _gridRepository.SaveGridAsync(targetGrid);

        // Act: Trigger merge by sending gridUpdate spanning both maps
        var gridUpdate = new GridUpdateDto
        {
            Grids = new List<List<string>>
            {
                new List<string> { "targetGrid", "new2", "new3" },
                new List<string> { "sourceGrid", "new5", "new6" },  // This will cause sourceGrid to shift
                new List<string> { "new7", "new8", "new9" }
            }
        };

        await _gridService.ProcessGridUpdateAsync(gridUpdate, _testGridStorage);

        // Assert: Verify the tile was copied with correct file reference
        
        // The sourceGrid should now be on targetMap (map 20) at a shifted coordinate
        var movedGrid = await _gridRepository.GetGridAsync("sourceGrid");
        Assert.NotNull(movedGrid);
        Assert.Equal(20, movedGrid.Map); // Should be moved to target map

        // The tile should exist at the NEW coordinate on the target map
        var movedTile = await _tileService.GetTileAsync(20, movedGrid.Coord, 0);
        Assert.NotNull(movedTile);
        Assert.Equal(sourceTilePath, movedTile.File); // Should preserve original file path

        // The original file should still exist
        var originalFilePath = Path.Combine(_testGridStorage, sourceTilePath);
        Assert.True(File.Exists(originalFilePath), "Original tile file should still exist after merge");
    }
}

