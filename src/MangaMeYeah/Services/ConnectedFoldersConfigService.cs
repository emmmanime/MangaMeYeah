using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace MangaMeYeah.Services;

public static class ConnectedFoldersConfigService
{
    public static string GetConfigFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "connected_folders.txt");
    }

    public static void EnsureDefaultConfigFileExists()
    {
        string path = GetConfigFilePath();
        if (File.Exists(path)) return;

        try
        {
            const string template =
                "# =============================================================================\r\n" +
                "# MangaMeYeah! 連結画像検出 自動オン対象フォルダー設定ファイル (connected_folders.txt)\r\n" +
                "# =============================================================================\r\n" +
                "# 1行に1つずつフォルダーパスを記述してください。\r\n" +
                "# 指定されたフォルダー（およびそのサブフォルダー・内部書庫）を開いた際、\r\n" +
                "# 自動的に「連結画像検出」が ON に切り替わります。\r\n" +
                "#\r\n" +
                "# ・先頭が # または ; の行、および空行は無視されます。\r\n" +
                "# ・ワイルドカード (*, ?) も使用可能です。\r\n" +
                "#\r\n" +
                "# 記述例:\r\n" +
                "# D:\\Twitter\r\n" +
                "# C:\\Users\\username\\Downloads\\twitter*\r\n";

            File.WriteAllText(path, template, new UTF8Encoding(false));
        }
        catch
        {
            // Ignore write failure on read-only media
        }
    }

    public static List<string> LoadConfiguredFolderPatterns()
    {
        EnsureDefaultConfigFileExists();
        string path = GetConfigFilePath();
        var patterns = new List<string>();

        if (!File.Exists(path)) return patterns;

        try
        {
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim().Trim('"');
                if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                {
                    continue;
                }
                patterns.Add(line);
            }
        }
        catch
        {
            // Ignore read error
        }

        return patterns;
    }

    public static bool IsPathMatched(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return false;

        string cleanTarget = targetPath.Contains('|')
            ? ArchiveImageService.SplitArchiveEntryPath(targetPath).ArchivePath
            : targetPath;

        try
        {
            cleanTarget = Path.GetFullPath(cleanTarget).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            cleanTarget = cleanTarget.TrimEnd('\\', '/');
        }

        var patterns = LoadConfiguredFolderPatterns();
        if (patterns.Count == 0) return false;

        foreach (string rawPattern in patterns)
        {
            string pattern = rawPattern.Replace('/', '\\').TrimEnd('\\');
            if (string.IsNullOrEmpty(pattern)) continue;

            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                string regexPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".");
                if (Regex.IsMatch(cleanTarget, regexPattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                if (cleanTarget.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                    cleanTarget.StartsWith(pattern + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static void OpenConfigFileInEditor(Window? owner = null)
    {
        EnsureDefaultConfigFileExists();
        string path = GetConfigFilePath();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            if (owner != null)
            {
                MessageBox.Show(owner, $"設定ファイルを開けませんでした:\n{path}\n\n{ex.Message}", "連結画像検出 自動オン設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
