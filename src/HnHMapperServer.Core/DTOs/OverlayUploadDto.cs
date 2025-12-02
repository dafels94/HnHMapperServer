namespace HnHMapperServer.Core.DTOs;

/// <summary>
/// Request payload for overlay upload from game clients
/// </summary>
public class OverlayUploadDto
{
    public string GridId { get; set; } = string.Empty;
    public List<OverlayItemDto> Overlays { get; set; } = new();
}

/// <summary>
/// Single overlay item in the upload request
/// </summary>
public class OverlayItemDto
{
    public string Tag { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string? Color { get; set; }  // Ignored, kept for compatibility
    public List<int> Tiles { get; set; } = new();  // Indices 0-9999 (100x100 grid)
}

/// <summary>
/// SSE event DTO for overlay updates
/// </summary>
public class OverlayEventDto
{
    public int MapId { get; set; }
    public int CoordX { get; set; }
    public int CoordY { get; set; }
    public string OverlayType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}
