using System.IO;
using MangaMeYeah.Models;

namespace MangaMeYeah.Services;

public static class QuickSaveService
{
    public static (bool Success, string Message) ExecuteQuickSave(
        int slotNumber,
        IReadOnlyList<string> sourceFilePaths,
        ViewerSettings settings)
    {
        if (sourceFilePaths.Count == 0)
        {
            return (false, "保存対象の画像が開かれていません。");
        }

        var slot = settings.QuickSaveSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
        if (slot == null || string.IsNullOrWhiteSpace(slot.FolderPath))
        {
            return (false, $"クイックセーブ{slotNumber} の保存先フォルダが未設定です。「ファイル > クイックセーブ設定」から設定してください。");
        }

        try
        {
            if (!Directory.Exists(slot.FolderPath))
            {
                Directory.CreateDirectory(slot.FolderPath);
            }

            var savedNames = new List<string>();
            foreach (string srcPath in sourceFilePaths)
            {
                if (!File.Exists(srcPath)) continue;

                string fileName = Path.GetFileName(srcPath);
                if (slot.PrependFolderName)
                {
                    string? parentDir = Path.GetDirectoryName(srcPath);
                    string folderName = !string.IsNullOrEmpty(parentDir) ? Path.GetFileName(parentDir) : "";
                    if (!string.IsNullOrEmpty(folderName) && !fileName.StartsWith(folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = $"{folderName}_{fileName}";
                    }
                }

                string destPath = GetUniqueDestinationPath(slot.FolderPath, fileName);
                File.Copy(srcPath, destPath, overwrite: false);
                savedNames.Add(Path.GetFileName(destPath));
            }

            if (savedNames.Count == 0)
            {
                return (false, "保存できるファイルがありませんでした。");
            }

            string summary = savedNames.Count == 1
                ? savedNames[0]
                : $"{savedNames[0]} ほか計{savedNames.Count}枚";

            return (true, $"[QuickSave{slotNumber}] 保存完了: {summary} → {slot.FolderPath}");
        }
        catch (Exception ex)
        {
            return (false, $"[QuickSave{slotNumber}] 保存エラー: {ex.Message}");
        }
    }

    private static string GetUniqueDestinationPath(string folder, string fileName)
    {
        string destPath = Path.Combine(folder, fileName);
        if (!File.Exists(destPath))
        {
            return destPath;
        }

        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int counter = 1;
        while (true)
        {
            string candidate = Path.Combine(folder, $"{baseName} ({counter}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
            counter++;
        }
    }
}
