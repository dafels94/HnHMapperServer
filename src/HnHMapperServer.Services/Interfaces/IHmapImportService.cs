namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service for importing .hmap files exported from the Haven &amp; Hearth game client
/// </summary>
public interface IHmapImportService
{
    /// <summary>
    /// Imports an .hmap file into the map database
    /// </summary>
    /// <param name="hmapStream">Stream containing the .hmap file data</param>
    /// <param name="tenantId">Target tenant ID</param>
    /// <param name="mode">Import mode: Merge or CreateNew</param>
    /// <param name="gridStorage">Base path for grid storage</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="cancellationToken">Cancellation token for aborting import</param>
    /// <returns>Import result with statistics</returns>
    Task<HmapImportResult> ImportAsync(
        Stream hmapStream,
        string tenantId,
        HmapImportMode mode,
        string gridStorage,
        IProgress<HmapImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up a failed or canceled import by removing all created items.
    /// </summary>
    /// <param name="mapIds">Map IDs to delete</param>
    /// <param name="gridIds">Grid IDs to delete</param>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="gridStorage">Base path for grid storage</param>
    Task CleanupFailedImportAsync(
        IEnumerable<int> mapIds,
        IEnumerable<string> gridIds,
        string tenantId,
        string gridStorage);
}

/// <summary>
/// Import mode selection
/// </summary>
public enum HmapImportMode
{
    /// <summary>
    /// Merge with existing maps - skip grids that already exist, add new grids to matching maps
    /// </summary>
    Merge,

    /// <summary>
    /// Create new maps - always create new maps for each segment, import all grids
    /// </summary>
    CreateNew
}

/// <summary>
/// Result of an .hmap import operation
/// </summary>
public class HmapImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Number of new maps created</summary>
    public int MapsCreated { get; set; }

    /// <summary>Number of grids imported</summary>
    public int GridsImported { get; set; }

    /// <summary>Number of grids skipped (already exist)</summary>
    public int GridsSkipped { get; set; }

    /// <summary>Number of tiles rendered</summary>
    public int TilesRendered { get; set; }

    /// <summary>Number of markers imported</summary>
    public int MarkersImported { get; set; }

    /// <summary>Number of markers skipped (no grid found)</summary>
    public int MarkersSkipped { get; set; }

    /// <summary>List of map IDs created or updated</summary>
    public List<int> AffectedMapIds { get; set; } = new();

    /// <summary>List of map IDs that were newly created (for cleanup)</summary>
    public List<int> CreatedMapIds { get; set; } = new();

    /// <summary>List of grid IDs that were created (for cleanup)</summary>
    public List<string> CreatedGridIds { get; set; } = new();

    /// <summary>Total bytes of storage used</summary>
    public long StorageBytesUsed { get; set; }

    /// <summary>Import duration</summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Progress information during import
/// </summary>
public class HmapImportProgress
{
    public string Phase { get; set; } = "";
    public int CurrentItem { get; set; }
    public int TotalItems { get; set; }
    public string CurrentItemName { get; set; } = "";
}
