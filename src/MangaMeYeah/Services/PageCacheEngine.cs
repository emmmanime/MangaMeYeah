using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using MangaMeYeah.Models;
using PhotoSauce.MagicScaler;

namespace MangaMeYeah.Services;

public sealed class CachedPageEntry
{
    public required string FilePath { get; init; }
    public required BitmapSource Bitmap { get; init; }
    public required int OriginalWidth { get; init; }
    public required int OriginalHeight { get; init; }
    public required long EstimatedMemoryBytes { get; init; }
    public string PixelFormatInfo { get; init; } = "24[bpp] (BGR24)";
    public int BitsPerPixel { get; init; } = 24;
    public string FormatShortName { get; init; } = "BGR24";
    public long LoadTimeMs { get; init; }
    public int UniqueColorCount { get; init; }
    public DateTime LastAccessedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PageCacheEngine : IDisposable
{
    private readonly ConcurrentDictionary<string, CachedPageEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int Width, int Height)> _dimensionCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _preloadCts;
    private int _cacheHits;
    private int _cacheMisses;

    public int CachedPageCount => _cache.Count;
    public long TotalEstimatedMemoryBytes => _cache.Values.Sum(v => v.EstimatedMemoryBytes);
    public int CacheHits => _cacheHits;
    public int CacheMisses => _cacheMisses;

    public (int Width, int Height) GetImageDimensionsFast(string filePath)
    {
        if (_dimensionCache.TryGetValue(filePath, out var dims))
        {
            return dims;
        }

        try
        {
            using var stream = OpenImageStream(filePath);
            var info = ImageFileInfo.Load(stream);
            if (info.Frames.Count > 0)
            {
                var f = info.Frames[0];
                dims = (f.Width, f.Height);
                _dimensionCache[filePath] = dims;
                return dims;
            }
        }
        catch
        {
            try
            {
                using var stream = OpenImageStream(filePath);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    dims = (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
                    _dimensionCache[filePath] = dims;
                    return dims;
                }
            }
            catch
            {
                // Ignore
            }
        }

        return (1, 1);
    }

    public bool IsLandscape(string filePath)
    {
        var (w, h) = GetImageDimensionsFast(filePath);
        return w > h;
    }

    public CachedPageEntry GetOrLoadPage(
        string filePath,
        int targetMaxWidth,
        int targetMaxHeight,
        ViewerSettings settings,
        bool countColors = false)
    {
        int effW = settings.ResizeCacheEnabled ? targetMaxWidth : 0;
        int effH = settings.ResizeCacheEnabled ? targetMaxHeight : 0;
        string key = BuildCacheKey(filePath, effW, effH, settings.Filter);
        if (_cache.TryGetValue(key, out var existing))
        {
            Interlocked.Increment(ref _cacheHits);
            existing.LastAccessedUtc = DateTime.UtcNow;
            if (countColors && existing.UniqueColorCount == 0)
            {
                // On-demand color count calculation only when requested (e.g. ImageInfoWindow)
                var reloaded = DecodeImage(filePath, effW, effH, settings.Filter, countColors: true);
                _cache[key] = reloaded;
                return reloaded;
            }
            return existing;
        }

        Interlocked.Increment(ref _cacheMisses);
        var loaded = DecodeImage(filePath, effW, effH, settings.Filter, countColors: countColors);
        _cache[key] = loaded;
        EnforceMemoryLimit(settings.CacheMemoryLimitMb, settings.MaxCachedBitmapCount);
        return loaded;
    }

    public void TriggerPreload(
        IReadOnlyList<string> files,
        int currentIndex,
        int targetMaxWidth,
        int targetMaxHeight,
        ViewerSettings settings)
    {
        _preloadCts?.Cancel();
        if (!settings.PreloadEnabled || files.Count == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _preloadCts = cts;
        var token = cts.Token;

        int effW = settings.ResizeCacheEnabled ? targetMaxWidth : 0;
        int effH = settings.ResizeCacheEnabled ? targetMaxHeight : 0;
        int forward = Math.Max(0, settings.PreloadForwardCount);
        int backward = Math.Max(0, settings.PreloadBackwardCount);

        var indicesToPreload = new List<int>();
        for (int i = 1; i <= forward; i++)
        {
            int idx = currentIndex + i;
            if (idx >= 0 && idx < files.Count) indicesToPreload.Add(idx);
        }
        for (int i = 1; i <= backward; i++)
        {
            int idx = currentIndex - i;
            if (idx >= 0 && idx < files.Count) indicesToPreload.Add(idx);
        }

        Task.Run(() =>
        {
            foreach (int idx in indicesToPreload)
            {
                if (token.IsCancellationRequested) break;
                string path = files[idx];
                string key = BuildCacheKey(path, effW, effH, settings.Filter);
                if (!_cache.ContainsKey(key))
                {
                    try
                    {
                        var entry = DecodeImage(path, effW, effH, settings.Filter, countColors: false);
                        if (!token.IsCancellationRequested)
                        {
                            _cache[key] = entry;
                            EnforceMemoryLimit(settings.CacheMemoryLimitMb, settings.MaxCachedBitmapCount);
                        }
                    }
                    catch
                    {
                        // Ignore preload decode error on individual file
                    }
                }
            }
        }, token);
    }

    public Task<BitmapSource?> LoadThumbnailAsync(string filePath, int thumbSize, CancellationToken cancellationToken = default)
    {
        return Task.Run<BitmapSource?>(() =>
        {
            if (cancellationToken.IsCancellationRequested) return null;
            try
            {
                var entry = DecodeImage(filePath, thumbSize, thumbSize, InterpolationFilter.Lanczos, countColors: false);
                return entry.Bitmap;
            }
            catch
            {
                return null;
            }
        }, cancellationToken);
    }

    public void Clear()
    {
        _preloadCts?.Cancel();
        _cache.Clear();
    }

    private CachedPageEntry DecodeImage(
        string filePath,
        int targetMaxWidth,
        int targetMaxHeight,
        InterpolationFilter filter,
        bool countColors = false)
    {
        var sw = Stopwatch.StartNew();
        var (origW, origH) = GetImageDimensionsFast(filePath);

        try
        {
            var processSettings = new ProcessImageSettings
            {
                Width = targetMaxWidth > 0 ? Math.Min(origW, Math.Max(64, targetMaxWidth)) : 0,
                Height = targetMaxHeight > 0 ? Math.Min(origH, Math.Max(64, targetMaxHeight)) : 0,
                ResizeMode = CropScaleMode.Max,
                // Use full high-precision Lanczos/Cubic pipeline rather than hybrid low-res DCT scale
                HybridMode = HybridScaleMode.Off,
                Sharpen = filter is InterpolationFilter.AverageSharpLight or InterpolationFilter.AverageSharpStrong
            };

            if (filter == InterpolationFilter.AverageSharpStrong)
            {
                processSettings.UnsharpMask = new UnsharpMaskSettings(120, 0.7, 2);
            }
            else if (filter == InterpolationFilter.AverageSharpLight)
            {
                processSettings.UnsharpMask = new UnsharpMaskSettings(60, 0.5, 3);
            }

            using var stream = OpenImageStream(filePath);
            using var pipeline = MagicImageProcessor.BuildPipeline(stream, processSettings);

            var source = pipeline.PixelSource;
            int w = source.Width;
            int h = source.Height;

            System.Windows.Media.PixelFormat wpfFormat;
            int bytesPerPixel;
            int bpp;
            string shortFormat;

            if (source.Format == PhotoSauce.MagicScaler.PixelFormats.Bgra32bpp)
            {
                wpfFormat = System.Windows.Media.PixelFormats.Bgra32;
                bytesPerPixel = 4;
                bpp = 32;
                shortFormat = "BGR32";
            }
            else if (source.Format == PhotoSauce.MagicScaler.PixelFormats.Bgr24bpp)
            {
                wpfFormat = System.Windows.Media.PixelFormats.Bgr24;
                bytesPerPixel = 3;
                bpp = 24;
                shortFormat = "BGR24";
            }
            else if (source.Format == PhotoSauce.MagicScaler.PixelFormats.Grey8bpp)
            {
                wpfFormat = System.Windows.Media.PixelFormats.Gray8;
                bytesPerPixel = 1;
                bpp = 8;
                shortFormat = "GRAY8";
            }
            else
            {
                throw new NotSupportedException("Fallback to WIC for custom pixel format");
            }

            int stride = (w * bytesPerPixel + 3) & ~3;
            int bufferLength = stride * h;

            byte[] pooledBuffer = ArrayPool<byte>.Shared.Rent(bufferLength);
            try
            {
                source.CopyPixels(new System.Drawing.Rectangle(0, 0, w, h), stride, pooledBuffer.AsSpan(0, bufferLength));
                var bitmap = BitmapSource.Create(
                    w, h,
                    96, 96,
                    wpfFormat,
                    null,
                    pooledBuffer,
                    stride);
                bitmap.Freeze();
                sw.Stop();

                int uniqueColors = countColors
                    ? CountUniqueColorsFast(pooledBuffer.AsSpan(0, bufferLength), w, h, stride, bytesPerPixel)
                    : 0;

                _dimensionCache[filePath] = (origW, origH);
                // Match MangaMeeya's uncompressed 32bpp/24bpp cache size convention
                long estCacheBytes = (long)w * h * 4;
                return new CachedPageEntry
                {
                    FilePath = filePath,
                    Bitmap = bitmap,
                    OriginalWidth = origW,
                    OriginalHeight = origH,
                    EstimatedMemoryBytes = estCacheBytes,
                    BitsPerPixel = bpp,
                    FormatShortName = shortFormat,
                    PixelFormatInfo = $"{bpp}[bpp] ({shortFormat})",
                    LoadTimeMs = Math.Max(1, sw.ElapsedMilliseconds),
                    UniqueColorCount = uniqueColors
                };
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pooledBuffer);
            }
        }
        catch
        {
            using var fallbackStream = OpenImageStream(filePath);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = fallbackStream;
            if (targetMaxWidth > 0 && origW > targetMaxWidth)
            {
                bmp.DecodePixelWidth = targetMaxWidth;
            }
            bmp.EndInit();
            bmp.Freeze();
            sw.Stop();

            int actualW = origW > 1 ? origW : bmp.PixelWidth;
            int actualH = origH > 1 ? origH : bmp.PixelHeight;
            _dimensionCache[filePath] = (actualW, actualH);

            int bpp = Math.Max(8, bmp.Format.BitsPerPixel);
            int bytesPerPixel = Math.Max(1, (bpp + 7) / 8);
            long estBytes = (long)bmp.PixelWidth * bmp.PixelHeight * 4;

            int uniqueColors = 0;
            if (countColors && bytesPerPixel is 1 or 3 or 4)
            {
                int w = bmp.PixelWidth;
                int h = bmp.PixelHeight;
                int stride = (w * bytesPerPixel + 3) & ~3;
                int bufLen = stride * h;
                byte[] pooled = ArrayPool<byte>.Shared.Rent(bufLen);
                try
                {
                    bmp.CopyPixels(pooled, stride, 0);
                    uniqueColors = CountUniqueColorsFast(pooled.AsSpan(0, bufLen), w, h, stride, bytesPerPixel);
                }
                catch
                {
                    // Ignore color count failure
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(pooled);
                }
            }

            return new CachedPageEntry
            {
                FilePath = filePath,
                Bitmap = bmp,
                OriginalWidth = actualW,
                OriginalHeight = actualH,
                EstimatedMemoryBytes = estBytes,
                BitsPerPixel = bpp,
                FormatShortName = bmp.Format.ToString().ToUpperInvariant(),
                PixelFormatInfo = $"{bpp}[bpp] ({bmp.Format})",
                LoadTimeMs = Math.Max(1, sw.ElapsedMilliseconds),
                UniqueColorCount = uniqueColors
            };
        }
    }

    private static int CountUniqueColorsFast(ReadOnlySpan<byte> buffer, int width, int height, int stride, int bytesPerPixel)
    {
        if (bytesPerPixel == 1)
        {
            Span<bool> seen = stackalloc bool[256];
            int count = 0;
            for (int y = 0; y < height; y++)
            {
                var row = buffer.Slice(y * stride, width);
                for (int x = 0; x < width; x++)
                {
                    byte v = row[x];
                    if (!seen[v])
                    {
                        seen[v] = true;
                        count++;
                    }
                }
            }
            return count;
        }

        // 24-bit RGB bitset: 16,777,216 bits = 262,144 ulongs (2 MB rented from ArrayPool)
        const int bitsetLen = 262144;
        ulong[] bitset = ArrayPool<ulong>.Shared.Rent(bitsetLen);
        try
        {
            Array.Clear(bitset, 0, bitsetLen);
            int unique = 0;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x * bytesPerPixel;
                    int rgb = buffer[idx] | (buffer[idx + 1] << 8) | (buffer[idx + 2] << 16);
                    int wordIdx = rgb >> 6;
                    ulong mask = 1UL << (rgb & 63);
                    if ((bitset[wordIdx] & mask) == 0)
                    {
                        bitset[wordIdx] |= mask;
                        unique++;
                    }
                }
            }
            return unique;
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(bitset);
        }
    }

    private void EnforceMemoryLimit(int limitMb, int maxCount)
    {
        if (limitMb <= 0 && maxCount <= 0) return;
        long maxBytes = limitMb > 0 ? (long)limitMb * 1024 * 1024 : long.MaxValue;
        int effMaxCount = maxCount > 0 ? maxCount : int.MaxValue;

        while ((TotalEstimatedMemoryBytes > maxBytes || _cache.Count > effMaxCount) && _cache.Count > 4)
        {
            var oldest = _cache.OrderBy(kv => kv.Value.LastAccessedUtc).FirstOrDefault();
            if (oldest.Key != null)
            {
                _cache.TryRemove(oldest.Key, out _);
            }
            else
            {
                break;
            }
        }
    }

    private static Stream OpenImageStream(string filePath)
    {
        if (ArchiveImageService.IsArchiveEntryPath(filePath))
        {
            return ArchiveImageService.OpenEntryStream(filePath);
        }
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
    }

    private static string BuildCacheKey(string filePath, int targetW, int targetH, InterpolationFilter filter)
    {
        int bucketW = targetW > 0 ? ((targetW + 255) / 256) * 256 : 0;
        int bucketH = targetH > 0 ? ((targetH + 255) / 256) * 256 : 0;
        return $"{filePath}@@{bucketW}x{bucketH}@{(int)filter}";
    }

    public void Dispose()
    {
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _cache.Clear();
    }
}
