using System.IO;
using System.Runtime.InteropServices;
using MangaMeYeah.Models;

namespace MangaMeYeah.Services;

public sealed class NaturalStringComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return StrCmpLogicalW(x, y);
    }
}

public static class ImageFolderService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff",
        ".avif", ".jxl", ".heic", ".ico", ".wdp", ".hdp",
        // Camera RAW formats (WIC / MagicScaler / Windows Raw Extension)
        ".cr2", ".cr3", ".nef", ".arw", ".orf", ".raf", ".rw2", ".dng", ".pef"
    };

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store", "Thumbs.db", "desktop.ini"
    };

    public static bool IsSupportedImageFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        string fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) return false;

        // macOS AppleDouble resource fork (._filename) or ignored system files
        if (fileName.StartsWith("._", StringComparison.Ordinal) || IgnoredFileNames.Contains(fileName))
        {
            return false;
        }

        // Ignore anything inside __MACOSX directory
        if (filePath.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string ext = Path.GetExtension(fileName);
        return SupportedExtensions.Contains(ext);
    }

    public static List<string> GetImagesInFolder(string folderPath, FileSortMode sortMode = FileSortMode.PathNaturalAsc)
    {
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedImageFile)
            .ToList();

        return SortFiles(files, sortMode);
    }

    public static List<string> SortFiles(IEnumerable<string> files, FileSortMode sortMode)
    {
        var list = files.ToList();
        switch (sortMode)
        {
            case FileSortMode.PathNaturalAsc:
                list.Sort(NaturalStringComparer.Instance);
                break;
            case FileSortMode.PathNaturalDesc:
                list.Sort((a, b) => NaturalStringComparer.Instance.Compare(b, a));
                break;
            case FileSortMode.PathAsc:
                list.Sort(StringComparer.OrdinalIgnoreCase);
                break;
            case FileSortMode.PathDesc:
                list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(b, a));
                break;
            case FileSortMode.NameAsc:
                list.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                break;
            case FileSortMode.NameDesc:
                list.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(b), Path.GetFileName(a)));
                break;
            case FileSortMode.ExtensionAsc:
                list.Sort((a, b) =>
                {
                    int cmp = StringComparer.OrdinalIgnoreCase.Compare(Path.GetExtension(a), Path.GetExtension(b));
                    return cmp != 0 ? cmp : NaturalStringComparer.Instance.Compare(a, b);
                });
                break;
            case FileSortMode.ExtensionDesc:
                list.Sort((a, b) =>
                {
                    int cmp = StringComparer.OrdinalIgnoreCase.Compare(Path.GetExtension(b), Path.GetExtension(a));
                    return cmp != 0 ? cmp : NaturalStringComparer.Instance.Compare(b, a);
                });
                break;
            case FileSortMode.SizeAsc:
                list.Sort((a, b) => GetFileLengthSafe(a).CompareTo(GetFileLengthSafe(b)));
                break;
            case FileSortMode.SizeDesc:
                list.Sort((a, b) => GetFileLengthSafe(b).CompareTo(GetFileLengthSafe(a)));
                break;
            case FileSortMode.DateAsc:
                list.Sort((a, b) => GetLastWriteTimeSafe(a).CompareTo(GetLastWriteTimeSafe(b)));
                break;
            case FileSortMode.DateDesc:
                list.Sort((a, b) => GetLastWriteTimeSafe(b).CompareTo(GetLastWriteTimeSafe(a)));
                break;
            case FileSortMode.Random:
                Random.Shared.Shuffle(CollectionsMarshal.AsSpan(list));
                break;
        }
        return list;
    }

    public static string? GetSiblingFolder(string currentFolderPath, int offset)
    {
        try
        {
            string? parentDir = Path.GetDirectoryName(currentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                return null;
            }

            var siblingDirs = Directory.EnumerateDirectories(parentDir)
                .Where(d => !Path.GetFileName(d).Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, NaturalStringComparer.Instance)
                .ToList();

            int currentIndex = siblingDirs.FindIndex(d => string.Equals(
                Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(currentFolderPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));

            if (currentIndex < 0) return null;

            // Search in direction for a folder that actually contains supported images
            int step = offset >= 0 ? 1 : -1;
            int idx = currentIndex + step;
            while (idx >= 0 && idx < siblingDirs.Count)
            {
                string candidate = siblingDirs[idx];
                if (Directory.EnumerateFiles(candidate).Any(IsSupportedImageFile))
                {
                    return candidate;
                }
                idx += step;
            }
        }
        catch
        {
            // Ignore directory access errors
        }
        return null;
    }

    private static long GetFileLengthSafe(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0L; }
    }

    private static DateTime GetLastWriteTimeSafe(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }
}
