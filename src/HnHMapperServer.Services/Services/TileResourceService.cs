using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for fetching and caching tile textures from Haven &amp; Hearth server
/// </summary>
public class TileResourceService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly Dictionary<string, Image<Rgba32>> _imageCache = new();
    private const string BASE_URL = "https://www.havenandhearth.com/mt/r/";

    public TileResourceService(string cacheDir)
    {
        _cacheDir = cacheDir;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
        }
    }

    /// <summary>
    /// Get the local cache path for a resource
    /// </summary>
    public string GetCachePath(string resourceName)
    {
        var safeName = resourceName.Replace("/", "_").Replace("\\", "_") + ".png";
        return Path.Combine(_cacheDir, safeName);
    }

    /// <summary>
    /// Check if resource is cached locally
    /// </summary>
    public bool IsCached(string resourceName)
    {
        return File.Exists(GetCachePath(resourceName));
    }

    /// <summary>
    /// Fetch a tile texture from server or cache
    /// </summary>
    public async Task<Image<Rgba32>?> GetTileImageAsync(string resourceName)
    {
        // Check in-memory cache first
        if (_imageCache.TryGetValue(resourceName, out var cached))
            return cached;

        var cachePath = GetCachePath(resourceName);

        // Check disk cache
        if (File.Exists(cachePath))
        {
            try
            {
                var img = await Image.LoadAsync<Rgba32>(cachePath);
                _imageCache[resourceName] = img;
                return img;
            }
            catch
            {
                // Corrupted cache file, try to re-download
            }
        }

        // Download from server
        var url = BASE_URL + resourceName;
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync();

            // Verify it's a PNG (magic bytes: 89 50 4E 47)
            if (data.Length < 8 || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            {
                return null;
            }

            // Cache to disk
            await File.WriteAllBytesAsync(cachePath, data);

            // Load and cache in memory
            using var ms = new MemoryStream(data);
            var img = await Image.LoadAsync<Rgba32>(ms);
            _imageCache[resourceName] = img;
            return img;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pre-fetch multiple tile resources
    /// </summary>
    public async Task<int> PrefetchTilesAsync(
        IEnumerable<string> resourceNames,
        IProgress<(int current, int total, string name)>? progress = null,
        int maxConcurrency = 5)
    {
        var nameList = resourceNames.Where(n => !IsCached(n)).Distinct().ToList();
        if (nameList.Count == 0)
            return 0;

        var total = nameList.Count;
        var current = 0;
        var fetched = 0;

        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        foreach (var name in nameList)
        {
            var resourceName = name;
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var img = await GetTileImageAsync(resourceName);
                    if (img != null)
                        Interlocked.Increment(ref fetched);

                    var count = Interlocked.Increment(ref current);
                    progress?.Report((count, total, resourceName));
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        return fetched;
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public (int cachedCount, long totalSize) GetCacheStats()
    {
        if (!Directory.Exists(_cacheDir))
            return (0, 0);

        var files = Directory.GetFiles(_cacheDir, "*.png");
        var totalSize = files.Sum(f => new FileInfo(f).Length);
        return (files.Length, totalSize);
    }

    public void Dispose()
    {
        if (_imageCache != null)
        {
            foreach (var img in _imageCache.Values)
            {
                img?.Dispose();
            }
            _imageCache.Clear();
        }
        _httpClient?.Dispose();
    }
}
