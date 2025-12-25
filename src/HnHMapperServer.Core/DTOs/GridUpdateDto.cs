namespace HnHMapperServer.Core.DTOs;

/// <summary>
/// Grid update request from client
/// </summary>
public class GridUpdateDto
{
    public List<List<string>> Grids { get; set; } = new();
}

/// <summary>
/// Grid request response to client
/// </summary>
public class GridRequestDto
{
    public List<string> GridRequests { get; set; } = new();
    public int Map { get; set; }
    public Models.Coord Coords { get; set; } = new(0, 0);
}

/// <summary>
/// Tile cache update for SSE
/// </summary>
public class TileCacheDto
{
    public int M { get; set; } // MapId
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; } // Zoom
    public long T { get; set; } // Timestamp (Unix milliseconds)
}

/// <summary>
/// Map merge notification
/// </summary>
public class MergeDto
{
    public int From { get; set; }
    public int To { get; set; }
    public Models.Coord Shift { get; set; } = new(0, 0);

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}
