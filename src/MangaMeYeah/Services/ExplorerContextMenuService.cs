using System.Diagnostics;
using Microsoft.Win32;

namespace MangaMeYeah.Services;

public static class ExplorerContextMenuService
{
    private const string FolderShellKey = @"Software\Classes\Directory\shell\MangaMeYeah";
    private const string FolderBgShellKey = @"Software\Classes\Directory\Background\shell\MangaMeYeah";
    private const string FileShellKey = @"Software\Classes\*\shell\MangaMeYeah";

    public static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? string.Empty;
    }

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FolderShellKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) Register(bool includeFolder, bool includeFolderBackground, bool includeFiles)
    {
        string exePath = GetExecutablePath();
        if (string.IsNullOrEmpty(exePath))
        {
            return (false, "実行ファイルのパスが取得できませんでした。");
        }

        try
        {
            if (includeFolder)
            {
                RegisterShellEntry(FolderShellKey, "MangaMeYeahで開く(&M)", exePath, $"\"{exePath}\" \"%1\"");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(FolderShellKey, throwOnMissingSubKey: false);
            }

            if (includeFolderBackground)
            {
                RegisterShellEntry(FolderBgShellKey, "このフォルダをMangaMeYeahで開く(&M)", exePath, $"\"{exePath}\" \"%V\"");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(FolderBgShellKey, throwOnMissingSubKey: false);
            }

            if (includeFiles)
            {
                RegisterShellEntry(FileShellKey, "MangaMeYeahで開く(&M)", exePath, $"\"{exePath}\" \"%1\"");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(FileShellKey, throwOnMissingSubKey: false);
            }

            return (true, "エクスプローラーの右クリックメニュー設定を更新しました。");
        }
        catch (Exception ex)
        {
            return (false, $"レジストリ更新エラー: {ex.Message}");
        }
    }

    public static (bool Success, string Message) UnregisterAll()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(FolderShellKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(FolderBgShellKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(FileShellKey, throwOnMissingSubKey: false);
            return (true, "エクスプローラーの右クリックメニューから「MangaMeYeahで開く」をすべて削除しました。");
        }
        catch (Exception ex)
        {
            return (false, $"レジストリ削除エラー: {ex.Message}");
        }
    }

    private static void RegisterShellEntry(string baseKeyPath, string menuText, string iconPath, string commandString)
    {
        using var shellKey = Registry.CurrentUser.CreateSubKey(baseKeyPath);
        shellKey.SetValue("", menuText);
        shellKey.SetValue("Icon", $"\"{iconPath}\",0");

        using var cmdKey = shellKey.CreateSubKey("command");
        cmdKey.SetValue("", commandString);
    }
}
