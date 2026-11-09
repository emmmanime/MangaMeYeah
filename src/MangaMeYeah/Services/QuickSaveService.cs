using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MangaMeYeah.Models;

namespace MangaMeYeah.Services;

public static class QuickSaveService
{
    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private static readonly IntPtr IDI_WARNING = new(32515);
    public static (bool Success, string Message) ExecuteQuickSave(
        int slotNumber,
        IReadOnlyList<string> sourceFilePaths,
        ViewerSettings settings,
        Window? ownerWindow = null)
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
            int skippedOverwriteCount = 0;

            foreach (string srcPath in sourceFilePaths)
            {
                bool isArchiveEntry = ArchiveImageService.IsArchiveEntryPath(srcPath);
                if (!isArchiveEntry && !File.Exists(srcPath))
                {
                    continue;
                }

                string fileName;
                string prefixFolderName = string.Empty;

                if (isArchiveEntry)
                {
                    var (archPath, entryKey) = ArchiveImageService.SplitArchiveEntryPath(srcPath);
                    if (!File.Exists(archPath)) continue;
                    // For nested sub-archives (outer.zip|inner.zip|img.png), take the last entry file name
                    string lastPart = entryKey.Contains('|') ? entryKey[(entryKey.LastIndexOf('|') + 1)..] : entryKey;
                    fileName = Path.GetFileName(lastPart);
                    prefixFolderName = Path.GetFileNameWithoutExtension(archPath);
                }
                else
                {
                    fileName = Path.GetFileName(srcPath);
                    string? parentDir = Path.GetDirectoryName(srcPath);
                    prefixFolderName = !string.IsNullOrEmpty(parentDir) ? Path.GetFileName(parentDir) : string.Empty;
                }

                if (string.IsNullOrEmpty(fileName)) continue;

                if (slot.PrependFolderName &&
                    !string.IsNullOrEmpty(prefixFolderName) &&
                    !fileName.StartsWith(prefixFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    fileName = $"{prefixFolderName}_{fileName}";
                }

                string destPath = Path.Combine(slot.FolderPath, fileName);
                if (File.Exists(destPath))
                {
                    bool overwrite = ShowOverwriteConfirmDialog(ownerWindow, fileName);
                    if (!overwrite)
                    {
                        skippedOverwriteCount++;
                        continue;
                    }
                }

                if (isArchiveEntry)
                {
                    using var entryStream = ArchiveImageService.OpenEntryStream(srcPath);
                    using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    entryStream.CopyTo(outStream);
                }
                else
                {
                    File.Copy(srcPath, destPath, overwrite: true);
                }

                savedNames.Add(Path.GetFileName(destPath));
            }

            if (savedNames.Count == 0)
            {
                if (skippedOverwriteCount > 0)
                {
                    return (false, $"[QuickSave{slotNumber}] 上書き保存をキャンセルしました");
                }
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

    private static bool ShowOverwriteConfirmDialog(Window? owner, string fileName)
    {
        var dlg = new Window
        {
            Title = "上書き確認",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
            Topmost = owner?.Topmost ?? true,
            Background = System.Windows.Media.Brushes.White
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentPanel = new DockPanel { Margin = new Thickness(18, 20, 18, 18) };

        try
        {
            IntPtr hIcon = LoadIcon(IntPtr.Zero, IDI_WARNING);
            if (hIcon != IntPtr.Zero)
            {
                var iconSource = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                iconSource.Freeze();
                var iconImg = new System.Windows.Controls.Image
                {
                    Source = iconSource,
                    Width = 32,
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0)
                };
                DockPanel.SetDock(iconImg, Dock.Left);
                contentPanel.Children.Add(iconImg);
            }
        }
        catch
        {
            // Fallback if system icon fails
        }

        var msgBlock = new TextBlock
        {
            Text = $"\"{fileName}\"は既に存在します。\n上書きしますか？",
            FontSize = 12,
            Foreground = System.Windows.Media.Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        contentPanel.Children.Add(msgBlock);
        Grid.SetRow(contentPanel, 0);
        rootGrid.Children.Add(contentPanel);

        var buttonBarBorder = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var yesBtn = new Button
        {
            Content = "はい(_Y)",
            Width = 76,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        yesBtn.Click += (_, _) =>
        {
            dlg.DialogResult = true;
            dlg.Close();
        };

        var noBtn = new Button
        {
            Content = "いいえ(_N)",
            Width = 76,
            Height = 24,
            IsCancel = true
        };
        noBtn.Click += (_, _) =>
        {
            dlg.DialogResult = false;
            dlg.Close();
        };

        buttonStack.Children.Add(yesBtn);
        buttonStack.Children.Add(noBtn);
        buttonBarBorder.Child = buttonStack;
        Grid.SetRow(buttonBarBorder, 1);
        rootGrid.Children.Add(buttonBarBorder);

        dlg.Content = rootGrid;
        return dlg.ShowDialog() == true;
    }
}
