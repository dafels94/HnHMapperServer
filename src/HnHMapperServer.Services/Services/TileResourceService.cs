using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service for fetching and caching tile textures from Haven &amp; Hearth server.
/// Uses a bounded LRU cache to limit memory usage during large imports.
/// </summary>
public class TileResourceService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly LruImageCache _imageCache;
    private const string BASE_URL = "https://www.havenandhearth.com/mt/r/";
    private const int DEFAULT_CACHE_SIZE = 50; // ~2MB max for 50 tile textures

    public TileResourceService(string cacheDir, int memoryCacheSize = DEFAULT_CACHE_SIZE)
    {
        _cacheDir = cacheDir;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _imageCache = new LruImageCache(memoryCacheSize);

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
    /// Fetch a tile texture from server or cache.
    /// Returns a CLONE that the caller owns and must dispose.
    /// This ensures thread-safety when multiple tasks access the same tile.
    /// </summary>
    public async Task<Image<Rgba32>?> GetTileImageAsync(string resourceName)
    {
        // Check in-memory LRU cache first
        // Lock during clone to prevent disposal race condition
        lock (_imageCache)
        {
            var cached = _imageCache.Get(resourceName);
            if (cached != null)
            {
                try
                {
                    return cached.Clone();
                }
                catch
                {
                    // Image was disposed, will reload below
                }
            }
        }

        var cachePath = GetCachePath(resourceName);

        // Check disk cache
        if (File.Exists(cachePath))
        {
            try
            {
                var img = await Image.LoadAsync<Rgba32>(cachePath);
                lock (_imageCache)
                {
                    _imageCache.Add(resourceName, img);
                    return img.Clone();
                }
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
            using var response = await _httpClient.GetAsync(url);
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

            // Load and cache in memory (LRU cache takes ownership)
            using var ms = new MemoryStream(data);
            var img = await Image.LoadAsync<Rgba32>(ms);
            lock (_imageCache)
            {
                _imageCache.Add(resourceName, img);
                return img.Clone();
            }
        }
        catch (HttpRequestException ex)
        {
            // Network error - store error for first failure only
            if (_firstNetworkError == null)
            {
                _firstNetworkError = $"Failed to fetch tile from Haven server: {url}. " +
                    $"Error: {ex.Message}. " +
                    "Check if production server can reach https://www.havenandhearth.com";
            }
            return null;
        }
        catch (TaskCanceledException)
        {
            // Timeout
            if (_firstNetworkError == null)
            {
                _firstNetworkError = $"Timeout fetching tile from Haven server: {url}. " +
                    "The Haven server may be slow or unreachable.";
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string? _firstNetworkError;

    /// <summary>
    /// Get the first network error that occurred, if any
    /// </summary>
    public string? GetFirstNetworkError() => _firstNetworkError;

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
    public (int diskCachedCount, long diskTotalSize, int memoryCacheCount) GetCacheStats()
    {
        var diskCount = 0;
        long diskSize = 0;

        if (Directory.Exists(_cacheDir))
        {
            var files = Directory.GetFiles(_cacheDir, "*.png");
            diskCount = files.Length;
            diskSize = files.Sum(f => new FileInfo(f).Length);
        }

        return (diskCount, diskSize, _imageCache.Count);
    }

    /// <summary>
    /// Clears the in-memory image cache to free memory.
    /// Disk cache is preserved. Call this between import batches.
    /// </summary>
    public void ClearMemoryCache()
    {
        _imageCache.Clear();
    }

    /// <summary>
    /// Gets the current count of images in memory cache.
    /// </summary>
    public int MemoryCacheCount => _imageCache.Count;

    public void Dispose()
    {
        _imageCache?.Dispose();
        _httpClient?.Dispose();
    }
}
