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

    // In-memory byte cache for recent extractions (prevents redundant extractions between dimension-probe, render, loupe)
    private static readonly ConcurrentDictionary<string, byte[]> RecentEntryBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> CacheEvictionQueue = new();
    private const int MaxCachedEntries = 64;

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
        return $"{archivePath}|{NormalizeEntryKey(entryKey)}";
    }

    public static string NormalizeEntryKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return key.Replace('\\', '/').TrimStart('/');
    }

    public static List<string> GetImagesInArchive(string archivePath, FileSortMode sortMode = FileSortMode.PathNaturalAsc)
    {
        if (!File.Exists(archivePath)) return [];

        try
        {
            string ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext is ".zip" or ".cbz")
            {
                return GetImagesInZip(archivePath);
            }

            lock (ArchiveLock)
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var entryKeys = new List<string>();

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    string? rawKey = entry.Key;
                    if (string.IsNullOrEmpty(rawKey)) continue;

                    string normKey = NormalizeEntryKey(rawKey);

                    // Ignore macOS resource forks and system files
                    string fileName = Path.GetFileName(normKey);
                    if (fileName.StartsWith("._", StringComparison.Ordinal) ||
                        normKey.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ImageFolderService.IsSupportedImageFile(fileName))
                    {
                        entryKeys.Add(normKey);
                    }
                }

                entryKeys.Sort((a, b) => NaturalStringComparer.Instance.Compare(a, b));
                return entryKeys.Select(k => BuildArchiveEntryPath(archivePath, k)).ToList();
            }
        }
        catch
        {
            return [];
        }
    }

    private static List<string> GetImagesInZip(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var entryKeys = new List<string>();
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // Directory entry has empty Name
            string normKey = NormalizeEntryKey(entry.FullName);
            if (normKey.StartsWith("._", StringComparison.Ordinal) ||
                normKey.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(normKey).StartsWith("._", StringComparison.Ordinal))
            {
                continue;
            }

            if (ImageFolderService.IsSupportedImageFile(normKey))
            {
                entryKeys.Add(normKey);
            }
        }

        entryKeys.Sort((a, b) => NaturalStringComparer.Instance.Compare(a, b));
        return entryKeys.Select(k => BuildArchiveEntryPath(archivePath, k)).ToList();
    }

    public static Stream OpenEntryStream(string archiveEntryPath)
    {
        var (archivePath, rawEntryKey) = SplitArchiveEntryPath(archiveEntryPath);
        if (string.IsNullOrEmpty(rawEntryKey))
        {
            return File.OpenRead(archivePath);
        }

        string entryKey = NormalizeEntryKey(rawEntryKey);

        if (RecentEntryBytesCache.TryGetValue(archiveEntryPath, out var cachedBytes))
        {
            return new MemoryStream(cachedBytes, writable: false);
        }

        byte[] bytes;
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext is ".zip" or ".cbz")
        {
            using var zip = ZipFile.OpenRead(archivePath);
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(NormalizeEntryKey(e.FullName), entryKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Archive entry not found: {entryKey} in {archivePath}");

            using var entryStream = entry.Open();
            using var ms = new MemoryStream((int)Math.Max(1024, entry.Length));
            entryStream.CopyTo(ms);
            bytes = ms.ToArray();
        }
        else
        {
            lock (ArchiveLock)
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(NormalizeEntryKey(e.Key ?? string.Empty), entryKey, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException($"Archive entry not found: {entryKey} in {archivePath}");

                using var entryStream = entry.OpenEntryStream();
                using var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                bytes = ms.ToArray();
            }
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
}
