using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

    // Small in-memory byte cache for recent extractions (prevents redundant extractions between dimension-probe and render)
    private static readonly ConcurrentDictionary<string, byte[]> RecentEntryBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> CacheEvictionQueue = new();
    private const int MaxCachedEntries = 16;

    public static bool IsArchiveFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return ArchiveExtensions.Contains(ext) && File.Exists(path);
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
        return $"{archivePath}|{entryKey}";
    }

    public static List<string> GetImagesInArchive(string archivePath, FileSortMode sortMode = FileSortMode.PathNaturalAsc)
    {
        if (!File.Exists(archivePath)) return [];

        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entryKeys = new List<string>();

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                string? key = entry.Key;
                if (string.IsNullOrEmpty(key)) continue;

                // Ignore macOS resource forks and system files
                string fileName = Path.GetFileName(key);
                if (fileName.StartsWith("._", StringComparison.Ordinal) ||
                    key.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ImageFolderService.IsSupportedImageFile(key))
                {
                    entryKeys.Add(key);
                }
            }

            // Sort entries naturally
            entryKeys.Sort((a, b) => NaturalStringComparer.Instance.Compare(a, b));

            return entryKeys.Select(k => BuildArchiveEntryPath(archivePath, k)).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static Stream OpenEntryStream(string archiveEntryPath)
    {
        var (archivePath, entryKey) = SplitArchiveEntryPath(archiveEntryPath);
        if (string.IsNullOrEmpty(entryKey))
        {
            return File.OpenRead(archivePath);
        }

        if (RecentEntryBytesCache.TryGetValue(archiveEntryPath, out var cachedBytes))
        {
            return new MemoryStream(cachedBytes, writable: false);
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Key, entryKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Archive entry not found: {entryKey} in {archivePath}");

        using var entryStream = entry.OpenEntryStream();
        var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        byte[] bytes = ms.ToArray();

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
}
