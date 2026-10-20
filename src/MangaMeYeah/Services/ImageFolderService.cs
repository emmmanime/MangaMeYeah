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

    public static bool HasSubfoldersOrArchives(string folderPath, bool checkArchives = true)
    {
        try
        {
            if (Directory.EnumerateDirectories(folderPath).Any(d =>
                !Path.GetFileName(d).Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(d).StartsWith('.')))
            {
                return true;
            }

            if (checkArchives && Directory.EnumerateFiles(folderPath).Any(ArchiveImageService.IsArchiveFile))
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    public static List<string> GetImagesInFolder(
        string folderPath,
        FileSortMode sortMode = FileSortMode.PathNaturalAsc,
        bool includeSubfolders = true,
        bool includeFolderArchives = true,
        bool includeArchiveArchives = true)
    {
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        var files = new List<string>();
        EnumerateImagesHierarchical(folderPath, files, sortMode, includeSubfolders, includeFolderArchives, includeArchiveArchives);

        if (sortMode == FileSortMode.Random)
        {
            Random.Shared.Shuffle(CollectionsMarshal.AsSpan(files));
        }

        return files;
    }

    private static void EnumerateImagesHierarchical(
        string currentDir,
        List<string> accumulator,
        FileSortMode sortMode,
        bool includeSubfolders,
        bool includeFolderArchives,
        bool includeArchiveArchives)
    {
        bool isDescending = sortMode is FileSortMode.PathNaturalDesc or FileSortMode.PathDesc;

        // 1. Direct files in current folder
        var directFiles = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(currentDir))
            {
                if (IsSupportedImageFile(file))
                {
                    directFiles.Add(file);
                }
            }
        }
        catch { }

        // If ascending, root files (covers, etc.) come before subfolders
        if (!isDescending && directFiles.Count > 0)
        {
            accumulator.AddRange(SortFiles(directFiles, sortMode));
        }

        if (includeSubfolders)
        {
            try
            {
                var subDirs = Directory.EnumerateDirectories(currentDir)
                    .Where(d => !Path.GetFileName(d).Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(d).StartsWith('.'))
                    .ToList();

                var archives = includeFolderArchives
                    ? Directory.EnumerateFiles(currentDir).Where(ArchiveImageService.IsArchiveFile).ToList()
                    : new List<string>();

                var childContainers = subDirs.Concat(archives).ToList();
                childContainers.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));

                if (isDescending)
                {
                    childContainers.Reverse();
                }

                foreach (var item in childContainers)
                {
                    if (Directory.Exists(item))
                    {
                        EnumerateImagesHierarchical(item, accumulator, sortMode, includeSubfolders, includeFolderArchives, includeArchiveArchives);
                    }
                    else if (ArchiveImageService.IsArchiveFile(item))
                    {
                        var archiveImages = ArchiveImageService.GetImagesInArchive(item, sortMode, includeArchiveArchives);
                        accumulator.AddRange(archiveImages);
                    }
                }
            }
            catch { }
        }

        // If descending, root files come after subfolders
        if (isDescending && directFiles.Count > 0)
        {
            accumulator.AddRange(SortFiles(directFiles, sortMode));
        }
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
            {
                var withSize = list.Select(f => (Path: f, Size: GetFileLengthSafe(f))).ToList();
                withSize.Sort((a, b) => a.Size.CompareTo(b.Size));
                return withSize.Select(x => x.Path).ToList();
            }
            case FileSortMode.SizeDesc:
            {
                var withSize = list.Select(f => (Path: f, Size: GetFileLengthSafe(f))).ToList();
                withSize.Sort((a, b) => b.Size.CompareTo(a.Size));
                return withSize.Select(x => x.Path).ToList();
            }
            case FileSortMode.DateAsc:
            {
                var withDate = list.Select(f => (Path: f, Date: GetLastWriteTimeSafe(f))).ToList();
                withDate.Sort((a, b) => a.Date.CompareTo(b.Date));
                return withDate.Select(x => x.Path).ToList();
            }
            case FileSortMode.DateDesc:
            {
                var withDate = list.Select(f => (Path: f, Date: GetLastWriteTimeSafe(f))).ToList();
                withDate.Sort((a, b) => b.Date.CompareTo(a.Date));
                return withDate.Select(x => x.Path).ToList();
            }
            case FileSortMode.Random:
                Random.Shared.Shuffle(CollectionsMarshal.AsSpan(list));
                break;
        }
        return list;
    }

    public static List<string> SortFilesHierarchical(IEnumerable<string> files, FileSortMode sortMode)
    {
        var list = files.ToList();
        if (list.Count <= 1 || sortMode == FileSortMode.Random)
        {
            return SortFiles(list, sortMode);
        }

        static string GetContainerKey(string path)
        {
            int pipeIdx = path.LastIndexOf('|');
            if (pipeIdx >= 0)
            {
                return path[..pipeIdx];
            }
            return Path.GetDirectoryName(path) ?? string.Empty;
        }

        var groups = list.GroupBy(GetContainerKey).ToList();
        if (groups.Count <= 1)
        {
            return SortFiles(list, sortMode);
        }

        bool isDescending = sortMode is FileSortMode.PathNaturalDesc or FileSortMode.PathDesc;

        var sortedGroups = groups.Select(g => new
        {
            Container = g.Key,
            Files = SortFiles(g, sortMode)
        }).ToList();

        sortedGroups.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Container, b.Container));
        if (isDescending)
        {
            sortedGroups.Reverse();
        }

        return sortedGroups.SelectMany(g => g.Files).ToList();
    }

    public static List<string> SortFolders(IEnumerable<string> folders, FolderSortMode sortMode)
    {
        var list = folders.ToList();
        switch (sortMode)
        {
            case FolderSortMode.ExplorerName:
            case FolderSortMode.NaturalNumber:
                list.Sort((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                break;
            case FolderSortMode.Unicode:
                list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                break;
            case FolderSortMode.Random:
                Random.Shared.Shuffle(CollectionsMarshal.AsSpan(list));
                break;
            case FolderSortMode.Size:
                list.Sort((a, b) => GetFolderImmediateSizeSafe(a).CompareTo(GetFolderImmediateSizeSafe(b)));
                break;
            case FolderSortMode.Date:
                list.Sort((a, b) => GetDirectoryLastWriteTimeSafe(a).CompareTo(GetDirectoryLastWriteTimeSafe(b)));
                break;
        }
        return list;
    }

    public static string? GetSiblingFolder(string currentFolderPath, int offset, FolderSortMode sortMode = FolderSortMode.ExplorerName)
    {
        try
        {
            string? parentDir = Path.GetDirectoryName(currentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                return null;
            }

            var rawDirs = Directory.EnumerateDirectories(parentDir)
                .Where(d => !Path.GetFileName(d).Equals("__MACOSX", StringComparison.OrdinalIgnoreCase));
            var rawArchives = Directory.EnumerateFiles(parentDir)
                .Where(ArchiveImageService.IsArchiveFile);

            var siblingItems = SortFolders(rawDirs.Concat(rawArchives), sortMode);

            int currentIndex = siblingItems.FindIndex(d => string.Equals(
                Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(currentFolderPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));

            if (currentIndex < 0) return null;

            // Search in direction for a folder or archive that actually contains supported images
            int step = offset >= 0 ? 1 : -1;
            int idx = currentIndex + step;
            while (idx >= 0 && idx < siblingItems.Count)
            {
                string candidate = siblingItems[idx];
                if (ArchiveImageService.IsArchiveFile(candidate))
                {
                    return candidate;
                }
                if (Directory.Exists(candidate) && HasAnySupportedImages(candidate))
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

    public static bool HasAnySupportedImages(string dirPath, bool includeArchives = true, int maxDepth = 2)
    {
        if (maxDepth < 0) return false;
        try
        {
            foreach (string file in Directory.EnumerateFiles(dirPath))
            {
                if (IsSupportedImageFile(file) || (includeArchives && ArchiveImageService.IsArchiveFile(file)))
                {
                    return true;
                }
            }

            if (maxDepth > 0)
            {
                foreach (string sub in Directory.EnumerateDirectories(dirPath))
                {
                    string dirName = Path.GetFileName(sub);
                    if (dirName.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) || dirName.StartsWith('.'))
                    {
                        continue;
                    }
                    if (HasAnySupportedImages(sub, includeArchives, maxDepth - 1))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static long GetFolderImmediateSizeSafe(string dirPath)
    {
        try
        {
            long sum = 0;
            foreach (string f in Directory.EnumerateFiles(dirPath))
            {
                if (IsSupportedImageFile(f))
                {
                    sum += GetFileLengthSafe(f);
                }
            }
            return sum;
        }
        catch
        {
            return 0L;
        }
    }

    private static DateTime GetDirectoryLastWriteTimeSafe(string dirPath)
    {
        try { return Directory.GetLastWriteTimeUtc(dirPath); }
        catch { return DateTime.MinValue; }
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
