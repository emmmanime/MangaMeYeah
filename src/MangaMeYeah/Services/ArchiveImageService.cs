using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MangaMeYeah.Models;
using SharpCompress.Archives;

namespace MangaMeYeah.Services;

public static class ArchiveImageService
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z"
    };

    private static readonly object ArchiveLock = new();

    // Cache for sub-archive byte arrays extracted from outer archives (key = "outerPath|subArchiveKey")
    private static readonly ConcurrentDictionary<string, byte[]> SubArchiveBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> SubArchiveEvictionQueue = new();
    private const int MaxCachedSubArchives = 16;

    // Cache for decoded entry bytes (key = archiveEntryPath)
    private static readonly ConcurrentDictionary<string, byte[]> RecentEntryBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> CacheEvictionQueue = new();
    private const int MaxCachedEntries = 64;

    public static bool IsArchiveFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return ArchiveExtensions.Contains(ext) && File.Exists(path);
    }

    public static bool IsArchiveFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string ext = Path.GetExtension(name);
        return ArchiveExtensions.Contains(ext);
    }

    public static bool IsArchiveEntryPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.Contains('|');
    }

    public static (string ArchivePath, string EntryKey) SplitArchiveEntryPath(string path)
    {
        int idx = path.IndexOf('|');
        if (idx < 0) return (path, string.Empty);
        return (path[..idx], path[(idx + 1)..]);
    }

    public static string BuildArchiveEntryPath(string archivePath, string entryKey)
    {
        return $"{archivePath}|{NormalizeEntryKey(entryKey)}";
    }

    public static string NormalizeEntryKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return key.Replace('\\', '/').TrimStart('/');
    }

    public static List<string> GetImagesInArchive(
        string archivePath,
        FileSortMode sortMode = FileSortMode.PathNaturalAsc,
        bool includeSubArchives = true)
    {
        if (!File.Exists(archivePath)) return [];

        try
        {
            string ext = Path.GetExtension(archivePath).ToLowerInvariant();
            List<string> entries;
            if (ext is ".zip" or ".cbz")
            {
                entries = GetImagesInZip(archivePath, includeSubArchives);
            }
            else
            {
                entries = GetImagesInSharpCompressArchive(archivePath, includeSubArchives);
            }

            return ImageFolderService.SortFilesHierarchical(entries, sortMode);
        }
        catch
        {
            return [];
        }
    }

    private static List<string> GetImagesInZip(string archivePath, bool includeSubArchives)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var result = new List<string>();

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string normKey = NormalizeEntryKey(entry.FullName);
            string fileName = Path.GetFileName(normKey);

            if (fileName.StartsWith("._", StringComparison.Ordinal) ||
                normKey.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ImageFolderService.IsSupportedImageFile(normKey))
            {
                result.Add(BuildArchiveEntryPath(archivePath, normKey));
            }
            else if (includeSubArchives && IsArchiveFileName(fileName))
            {
                // Inner sub-archive found inside zip!
                try
                {
                    using var subStream = entry.Open();
                    using var ms = new MemoryStream((int)Math.Max(1024, entry.Length));
                    subStream.CopyTo(ms);
                    byte[] subBytes = ms.ToArray();

                    // Cache sub-archive bytes
                    string subCacheKey = $"{archivePath}|{normKey}";
                    CacheSubArchiveBytes(subCacheKey, subBytes);

                    var innerImages = GetImagesFromMemoryArchive(subBytes, fileName, includeSubArchives: false);
                    foreach (var innerKey in innerImages)
                    {
                        result.Add(BuildArchiveEntryPath(archivePath, $"{normKey}|{innerKey}"));
                    }
                }
                catch
                {
                    // Ignore corrupted inner archive
                }
            }
        }

        return result;
    }

    private static List<string> GetImagesInSharpCompressArchive(string archivePath, bool includeSubArchives)
    {
        lock (ArchiveLock)
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var result = new List<string>();

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                string? rawKey = entry.Key;
                if (string.IsNullOrEmpty(rawKey)) continue;

                string normKey = NormalizeEntryKey(rawKey);
                string fileName = Path.GetFileName(normKey);

                if (fileName.StartsWith("._", StringComparison.Ordinal) ||
                    normKey.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ImageFolderService.IsSupportedImageFile(fileName))
                {
                    result.Add(BuildArchiveEntryPath(archivePath, normKey));
                }
                else if (includeSubArchives && IsArchiveFileName(fileName))
                {
                    try
                    {
                        using var subStream = entry.OpenEntryStream();
                        using var ms = new MemoryStream();
                        subStream.CopyTo(ms);
                        byte[] subBytes = ms.ToArray();

                        string subCacheKey = $"{archivePath}|{normKey}";
                        CacheSubArchiveBytes(subCacheKey, subBytes);

                        var innerImages = GetImagesFromMemoryArchive(subBytes, fileName, includeSubArchives: false);
                        foreach (var innerKey in innerImages)
                        {
                            result.Add(BuildArchiveEntryPath(archivePath, $"{normKey}|{innerKey}"));
                        }
                    }
                    catch
                    {
                        // Ignore corrupted inner archive
                    }
                }
            }

            return result;
        }
    }

    private static List<string> GetImagesFromMemoryArchive(byte[] archiveBytes, string archiveName, bool includeSubArchives)
    {
        string ext = Path.GetExtension(archiveName).ToLowerInvariant();
        var innerKeys = new List<string>();

        if (ext is ".zip" or ".cbz")
        {
            using var ms = new MemoryStream(archiveBytes, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string normKey = NormalizeEntryKey(entry.FullName);
                string fileName = Path.GetFileName(normKey);

                if (fileName.StartsWith("._", StringComparison.Ordinal) ||
                    normKey.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ImageFolderService.IsSupportedImageFile(normKey))
                {
                    innerKeys.Add(normKey);
                }
            }
        }
        else
        {
            lock (ArchiveLock)
            {
                using var ms = new MemoryStream(archiveBytes, writable: false);
                using var archive = ArchiveFactory.OpenArchive(ms);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    string? rawKey = entry.Key;
                    if (string.IsNullOrEmpty(rawKey)) continue;

                    string normKey = NormalizeEntryKey(rawKey);
                    string fileName = Path.GetFileName(normKey);

                    if (fileName.StartsWith("._", StringComparison.Ordinal) ||
                        normKey.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ImageFolderService.IsSupportedImageFile(fileName))
                    {
                        innerKeys.Add(normKey);
                    }
                }
            }
        }

        innerKeys.Sort((a, b) => NaturalStringComparer.Instance.Compare(a, b));
        return innerKeys;
    }

    public static Stream OpenEntryStream(string archiveEntryPath)
    {
        if (RecentEntryBytesCache.TryGetValue(archiveEntryPath, out var cachedBytes))
        {
            return new MemoryStream(cachedBytes, writable: false);
        }

        string[] parts = archiveEntryPath.Split('|');
        if (parts.Length <= 1)
        {
            return File.OpenRead(parts[0]);
        }

        string outerArchive = parts[0];
        byte[] bytes;

        if (parts.Length == 2)
        {
            // Standard single archive: outer.zip|entry.jpg
            string entryKey = NormalizeEntryKey(parts[1]);
            bytes = ExtractEntryBytesFromOuter(outerArchive, entryKey);
        }
        else
        {
            // Sub-archive nested: outer.zip|subArchive.zip|entry.jpg (or deeper)
            string subArchiveKey = NormalizeEntryKey(parts[1]);
            string innerEntryKey = NormalizeEntryKey(string.Join('/', parts.Skip(2)));

            string subCacheKey = $"{outerArchive}|{subArchiveKey}";
            if (!SubArchiveBytesCache.TryGetValue(subCacheKey, out var subBytes))
            {
                subBytes = ExtractEntryBytesFromOuter(outerArchive, subArchiveKey);
                CacheSubArchiveBytes(subCacheKey, subBytes);
            }

            bytes = ExtractEntryBytesFromMemoryArchive(subBytes, subArchiveKey, innerEntryKey);
        }

        RecentEntryBytesCache[archiveEntryPath] = bytes;
        CacheEvictionQueue.Enqueue(archiveEntryPath);
        while (CacheEvictionQueue.Count > MaxCachedEntries)
        {
            if (CacheEvictionQueue.TryDequeue(out var oldKey))
            {
                RecentEntryBytesCache.TryRemove(oldKey, out _);
            }
        }

        return new MemoryStream(bytes, writable: false);
    }

    private static byte[] ExtractEntryBytesFromOuter(string archivePath, string entryKey)
    {
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext is ".zip" or ".cbz")
        {
            using var zip = ZipFile.OpenRead(archivePath);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(NormalizeEntryKey(e.FullName), entryKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Archive entry not found: {entryKey} in {archivePath}");

            using var entryStream = entry.Open();
            using var ms = new MemoryStream((int)Math.Max(1024, entry.Length));
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }

        lock (ArchiveLock)
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(NormalizeEntryKey(e.Key ?? string.Empty), entryKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Archive entry not found: {entryKey} in {archivePath}");

            using var entryStream = entry.OpenEntryStream();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    private static byte[] ExtractEntryBytesFromMemoryArchive(byte[] archiveBytes, string archiveName, string entryKey)
    {
        string ext = Path.GetExtension(archiveName).ToLowerInvariant();
        if (ext is ".zip" or ".cbz")
        {
            using var msIn = new MemoryStream(archiveBytes, writable: false);
            using var zip = new ZipArchive(msIn, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(NormalizeEntryKey(e.FullName), entryKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Inner archive entry not found: {entryKey} in {archiveName}");

            using var entryStream = entry.Open();
            using var msOut = new MemoryStream((int)Math.Max(1024, entry.Length));
            entryStream.CopyTo(msOut);
            return msOut.ToArray();
        }

        lock (ArchiveLock)
        {
            using var msIn = new MemoryStream(archiveBytes, writable: false);
            using var archive = ArchiveFactory.OpenArchive(msIn);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(NormalizeEntryKey(e.Key ?? string.Empty), entryKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Inner archive entry not found: {entryKey} in {archiveName}");

            using var entryStream = entry.OpenEntryStream();
            using var msOut = new MemoryStream();
            entryStream.CopyTo(msOut);
            return msOut.ToArray();
        }
    }

    private static void CacheSubArchiveBytes(string key, byte[] bytes)
    {
        SubArchiveBytesCache[key] = bytes;
        SubArchiveEvictionQueue.Enqueue(key);
        while (SubArchiveEvictionQueue.Count > MaxCachedSubArchives)
        {
            if (SubArchiveEvictionQueue.TryDequeue(out var oldKey))
            {
                SubArchiveBytesCache.TryRemove(oldKey, out _);
            }
        }
    }
}
