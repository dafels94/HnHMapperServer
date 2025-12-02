using HnHMapperServer.Core.Models;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Tracks grids, tiles, and overlays for batch database operations during import.
/// Accumulates items until batch size is reached, then provides them for bulk save.
/// </summary>
public class BatchImportContext : IDisposable
{
    private readonly List<GridData> _gridBatch = new();
    private readonly List<TileData> _tileBatch = new();
    private readonly List<OverlayData> _overlayBatch = new();
    private double _accumulatedStorageMB;
    private readonly int _batchSize;

    public BatchImportContext(int batchSize = 500)
    {
        _batchSize = batchSize;
    }

    /// <summary>
    /// Number of grids pending in the current batch.
    /// </summary>
    public int PendingGrids => _gridBatch.Count;

    /// <summary>
    /// Number of tiles pending in the current batch.
    /// </summary>
    public int PendingTiles => _tileBatch.Count;

    /// <summary>
    /// Number of overlays pending in the current batch.
    /// </summary>
    public int PendingOverlays => _overlayBatch.Count;

    /// <summary>
    /// Total storage accumulated since last extraction.
    /// </summary>
    public double AccumulatedStorageMB => _accumulatedStorageMB;

    /// <summary>
    /// Adds a grid to the current batch.
    /// </summary>
    public void AddGrid(GridData grid) => _gridBatch.Add(grid);

    /// <summary>
    /// Adds a tile to the current batch.
    /// </summary>
    public void AddTile(TileData tile) => _tileBatch.Add(tile);

    /// <summary>
    /// Adds an overlay to the current batch.
    /// </summary>
    public void AddOverlay(OverlayData overlay) => _overlayBatch.Add(overlay);

    /// <summary>
    /// Adds to the accumulated storage counter.
    /// </summary>
    public void AddStorage(double sizeMB) => _accumulatedStorageMB += sizeMB;

    /// <summary>
    /// Returns true if the batch has reached the configured size and should be flushed.
    /// </summary>
    public bool ShouldFlush() => _gridBatch.Count >= _batchSize;

    /// <summary>
    /// Extracts the current batch contents and clears the internal lists.
    /// Returns the grids, tiles, overlays, and accumulated storage.
    /// </summary>
    public (List<GridData> Grids, List<TileData> Tiles, List<OverlayData> Overlays, double StorageMB) ExtractBatch()
    {
        var grids = _gridBatch.ToList();
        var tiles = _tileBatch.ToList();
        var overlays = _overlayBatch.ToList();
        var storage = _accumulatedStorageMB;

        _gridBatch.Clear();
        _tileBatch.Clear();
        _overlayBatch.Clear();
        _accumulatedStorageMB = 0;

        return (grids, tiles, overlays, storage);
    }

    /// <summary>
    /// Checks if there are any pending items in the batch.
    /// </summary>
    public bool HasPendingItems => _gridBatch.Count > 0 || _tileBatch.Count > 0 || _overlayBatch.Count > 0;

    /// <summary>
    /// Resets the context, clearing all pending items and accumulated storage.
    /// </summary>
    public void Reset()
    {
        _gridBatch.Clear();
        _tileBatch.Clear();
        _overlayBatch.Clear();
        _accumulatedStorageMB = 0;
    }

    private bool _disposed;

    /// <summary>
    /// Disposes the context, clearing all accumulated data.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
