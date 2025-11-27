using System.IO.Compression;
using System.Text;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Reads and parses .hmap files exported from Haven &amp; Hearth game client
/// </summary>
public class HmapReader
{
    private const string SIGNATURE = "Haven Mapfile 1";

    public HmapData Read(Stream stream)
    {
        var data = new HmapData();

        // Read signature (15 bytes - "Haven Mapfile 1")
        var sigBytes = new byte[15];
        stream.ReadExactly(sigBytes);
        var signature = Encoding.ASCII.GetString(sigBytes);

        if (signature != SIGNATURE)
        {
            throw new InvalidDataException($"Invalid signature: '{signature}'. Expected: '{SIGNATURE}'");
        }

        data.Signature = signature;

        // Rest is Z-compressed (zlib/deflate)
        // Skip the 2-byte zlib header (78 DA = best compression)
        var zlibHeader = new byte[2];
        stream.ReadExactly(zlibHeader);

        using var deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        deflateStream.CopyTo(ms);
        ms.Position = 0;

        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        // Read records
        while (ms.Position < ms.Length)
        {
            var recordType = ReadNullTerminatedString(reader);
            if (string.IsNullOrEmpty(recordType))
                break;

            var recordLength = reader.ReadInt32();
            var recordData = reader.ReadBytes(recordLength);

            if (recordType == "grid")
            {
                var grid = ParseGrid(recordData);
                data.Grids.Add(grid);
            }
            else if (recordType == "mark")
            {
                var marker = ParseMarker(recordData);
                if (marker != null)
                    data.Markers.Add(marker);
            }
            else
            {
                data.UnknownRecords.Add(recordType);
            }
        }

        return data;
    }

    private HmapGridData ParseGrid(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var grid = new HmapGridData();

        grid.Version = reader.ReadByte();
        grid.GridId = reader.ReadInt64();
        grid.SegmentId = reader.ReadInt64();
        grid.ModifiedTime = reader.ReadInt64();
        grid.TileX = reader.ReadInt32();
        grid.TileY = reader.ReadInt32();

        if (grid.Version >= 4)
        {
            grid.GridSize = reader.ReadInt32();

            // Read tilesets
            var tilesetCount = reader.ReadUInt16();
            for (int i = 0; i < tilesetCount; i++)
            {
                var tileset = new HmapTilesetInfo
                {
                    ResourceName = ReadNullTerminatedString(reader),
                    ResourceVersion = reader.ReadUInt16(),
                    Priority = reader.ReadByte()
                };
                grid.Tilesets.Add(tileset);
            }

            // Read tile indices
            var tileCount = grid.GridSize;
            grid.TileIndices = new int[tileCount];

            if (tilesetCount <= 256)
            {
                for (int i = 0; i < tileCount; i++)
                    grid.TileIndices[i] = reader.ReadByte();
            }
            else
            {
                for (int i = 0; i < tileCount; i++)
                    grid.TileIndices[i] = reader.ReadUInt16();
            }

            // Read z-map (height data) for cliff/ridge rendering
            if (ms.Position < ms.Length)
            {
                var zFormat = reader.ReadByte();
                grid.ZMap = new float[tileCount];

                switch (zFormat)
                {
                    case 0: // Uniform height - single value for entire grid
                        var uniformZ = reader.ReadSingle();
                        Array.Fill(grid.ZMap, uniformZ);
                        break;

                    case 1: // Byte-quantized with min + step
                        var minZ1 = reader.ReadSingle();
                        var stepZ1 = reader.ReadSingle();
                        for (int i = 0; i < tileCount; i++)
                            grid.ZMap[i] = minZ1 + reader.ReadByte() * stepZ1;
                        break;

                    case 2: // Word-quantized with min + step
                        var minZ2 = reader.ReadSingle();
                        var stepZ2 = reader.ReadSingle();
                        for (int i = 0; i < tileCount; i++)
                            grid.ZMap[i] = minZ2 + reader.ReadUInt16() * stepZ2;
                        break;

                    case 3: // Full precision - float per tile
                        for (int i = 0; i < tileCount; i++)
                            grid.ZMap[i] = reader.ReadSingle();
                        break;

                    default:
                        // Unknown format - skip z-map
                        grid.ZMap = null;
                        break;
                }
            }

            // Skip overlays - not needed for minimap rendering
            // Overlays are only used in 3D rendering, not minimaps
        }

        return grid;
    }

    private HmapMarkerData? ParseMarker(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var version = reader.ReadByte();
        if (version != 1)
            return null; // Unknown version

        var segmentId = reader.ReadInt64();
        var tileX = reader.ReadInt32();
        var tileY = reader.ReadInt32();
        var name = ReadNullTerminatedString(reader);
        var markerType = (char)reader.ReadByte();

        switch (markerType)
        {
            case 'p': // PMarker - player-placed marker
                return new HmapPMarker
                {
                    SegmentId = segmentId,
                    TileX = tileX,
                    TileY = tileY,
                    Name = name,
                    ColorR = reader.ReadByte(),
                    ColorG = reader.ReadByte(),
                    ColorB = reader.ReadByte(),
                    ColorA = reader.ReadByte()
                };

            case 's': // SMarker - system/game object (includes thingwalls)
                return new HmapSMarker
                {
                    SegmentId = segmentId,
                    TileX = tileX,
                    TileY = tileY,
                    Name = name,
                    ObjectId = reader.ReadInt64(),
                    ResourceName = ReadNullTerminatedString(reader),
                    ResourceVersion = reader.ReadUInt16()
                };

            default:
                return null; // Unknown marker type
        }
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}

/// <summary>
/// Parsed .hmap file data
/// </summary>
public class HmapData
{
    public string Signature { get; set; } = "";
    public List<HmapGridData> Grids { get; set; } = new();
    public List<HmapMarkerData> Markers { get; set; } = new();
    public List<string> UnknownRecords { get; set; } = new();

    /// <summary>
    /// Get all unique segment IDs (each segment is a separate map region)
    /// </summary>
    public IEnumerable<long> GetSegmentIds() => Grids.Select(g => g.SegmentId).Distinct();

    /// <summary>
    /// Get grids for a specific segment
    /// </summary>
    public List<HmapGridData> GetGridsForSegment(long segmentId) =>
        Grids.Where(g => g.SegmentId == segmentId).ToList();

    /// <summary>
    /// Get markers for a specific segment
    /// </summary>
    public List<HmapMarkerData> GetMarkersForSegment(long segmentId) =>
        Markers.Where(m => m.SegmentId == segmentId).ToList();
}

/// <summary>
/// Grid data from .hmap file
/// </summary>
public class HmapGridData
{
    public byte Version { get; set; }
    public long GridId { get; set; }
    public long SegmentId { get; set; }
    public long ModifiedTime { get; set; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int GridSize { get; set; }
    public List<HmapTilesetInfo> Tilesets { get; set; } = new();
    public int[]? TileIndices { get; set; }

    /// <summary>
    /// Height map data for cliff/ridge detection (100x100 floats)
    /// </summary>
    public float[]? ZMap { get; set; }

    /// <summary>
    /// Get GridId as string for storage in database
    /// </summary>
    public string GridIdString => GridId.ToString();
}

/// <summary>
/// Tileset information from .hmap grid
/// </summary>
public class HmapTilesetInfo
{
    public string ResourceName { get; set; } = "";
    public ushort ResourceVersion { get; set; }
    public byte Priority { get; set; }
}

/// <summary>
/// Base marker data from .hmap file
/// </summary>
public abstract class HmapMarkerData
{
    public long SegmentId { get; set; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Player-placed marker (PMarker)
/// </summary>
public class HmapPMarker : HmapMarkerData
{
    public byte ColorR { get; set; }
    public byte ColorG { get; set; }
    public byte ColorB { get; set; }
    public byte ColorA { get; set; }
}

/// <summary>
/// System/game object marker (SMarker) - includes thingwalls
/// </summary>
public class HmapSMarker : HmapMarkerData
{
    public long ObjectId { get; set; }
    public string ResourceName { get; set; } = "";
    public ushort ResourceVersion { get; set; }
}
