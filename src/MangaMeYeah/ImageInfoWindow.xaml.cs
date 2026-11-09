using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MangaMeYeah.Services;

namespace MangaMeYeah;

public sealed class DisplayedSlotInfo
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required int PageNumber { get; init; }
    public required int TotalPages { get; init; }
    public required CachedPageEntry Entry { get; set; }
    public required int DisplayedWidth { get; init; }
    public required int DisplayedHeight { get; init; }
}

public partial class ImageInfoWindow : Window
{
    private readonly MainWindow? _mainWindow;
    private int _lastColCount = -1;
    private int _lastRowCount = -1;

    public ImageInfoWindow(
        IReadOnlyList<DisplayedSlotInfo> slots,
        int totalRows,
        int totalColumns,
        MainWindow? mainWindow = null)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        UpdateSlots(slots, totalRows, totalColumns);
        PreviewKeyDown += ImageInfoWindow_PreviewKeyDown;
    }

    public void UpdateSlots(IReadOnlyList<DisplayedSlotInfo> slots, int totalRows, int totalColumns)
    {
        InfoLayoutGrid.Children.Clear();
        InfoLayoutGrid.RowDefinitions.Clear();
        InfoLayoutGrid.ColumnDefinitions.Clear();

        int rows = Math.Max(1, totalRows);
        int cols = Math.Max(1, totalColumns);

        // 横並びの枚数（1枚 / 横2・縦横2×2 / 横3 / 横4）に応じた値カラムの初期・最小幅
        double valueColMinWidth = cols switch
        {
            1 => 360,
            2 => 290,
            3 => 240,
            _ => 200
        };

        double singleCellMinWidth = 98 + valueColMinWidth + 6;

        for (int r = 0; r < rows; r++)
        {
            InfoLayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int c = 0; c < cols; c++)
        {
            InfoLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = singleCellMinWidth
            });
        }

        PopulateSlotGridRecursive(slots, 0, InfoLayoutGrid, valueColMinWidth);

        // 初回表示時、または列数・行数が変化した時（単ページ⇔見開き⇔縦横2×2⇔横4枚など）にウィンドウサイズを最適化
        if (_lastColCount != cols || _lastRowCount != rows)
        {
            _lastColCount = cols;
            _lastRowCount = rows;

            double screenMaxW = SystemParameters.WorkArea.Width * 0.95;
            double screenMaxH = SystemParameters.WorkArea.Height * 0.92;

            double desiredWidth = Math.Min(screenMaxW, cols * singleCellMinWidth + 32);
            double singleCellHeight = 10 * 19 + 56 + 6; // 10行(19px) + その他(56px) + 枠余白
            double desiredHeight = Math.Min(screenMaxH, rows * singleCellHeight + 78);

            MinWidth = Math.Min(screenMaxW, Math.Max(420, cols * 260 + 28));
            Width = Math.Max(MinWidth, desiredWidth);
            Height = Math.Max(310, desiredHeight);
        }
    }

    private void ImageInfoWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static void PopulateSlotGridRecursive(
        IReadOnlyList<DisplayedSlotInfo> slots,
        int currentIndex,
        Grid targetGrid,
        double valueColumnMinWidth)
    {
        if (currentIndex >= slots.Count)
        {
            return;
        }

        var slot = slots[currentIndex];
        FrameworkElement cellPanel = CreateSingleImageInfoTable(slot, valueColumnMinWidth);
        Grid.SetRow(cellPanel, slot.Row);
        Grid.SetColumn(cellPanel, slot.Column);
        targetGrid.Children.Add(cellPanel);

        // 次の枚数を再帰的に配置
        PopulateSlotGridRecursive(slots, currentIndex + 1, targetGrid, valueColumnMinWidth);
    }

    private static FrameworkElement CreateSingleImageInfoTable(DisplayedSlotInfo slot, double valueColumnMinWidth)
    {
        string filePath = slot.Entry.FilePath;
        string displayFileName;
        string displayPath;
        long fileSizeBytes = 0;
        string updateTimeStr = "-";

        if (ArchiveImageService.IsArchiveEntryPath(filePath))
        {
            var (archPath, entryKey) = ArchiveImageService.SplitArchiveEntryPath(filePath);
            if (entryKey.Contains('|'))
            {
                int lastPipe = entryKey.LastIndexOf('|');
                string subArch = entryKey[..lastPipe].Replace('/', '\\');
                string innerEntry = entryKey[(lastPipe + 1)..];
                displayFileName = innerEntry.Replace('/', '\\');
                displayPath = $"{archPath}\\{subArch}";
            }
            else
            {
                displayFileName = entryKey.Replace('/', '\\');
                displayPath = archPath;
            }

            if (ArchiveImageService.TryGetEntryMetadata(filePath, out long uncompSize, out DateTime? entryTime))
            {
                fileSizeBytes = uncompSize;
                if (entryTime.HasValue)
                {
                    updateTimeStr = entryTime.Value.ToString("yyyy年M月d日  H:mm:ss");
                }
            }
        }
        else
        {
            var fi = new FileInfo(filePath);
            displayFileName = fi.Name;
            displayPath = fi.DirectoryName ?? string.Empty;
            if (fi.Exists)
            {
                fileSizeBytes = fi.Length;
                updateTimeStr = fi.LastWriteTime.ToString("yyyy年M月d日  H:mm:ss");
            }
        }

        string origSizeStr = $"{slot.Entry.OriginalWidth}×{slot.Entry.OriginalHeight} {slot.Entry.PixelFormatInfo}";
        string dispSizeStr = $"{slot.DisplayedWidth}×{slot.DisplayedHeight} {slot.Entry.PixelFormatInfo}";
        string cacheSizeStr = $"{slot.Entry.EstimatedMemoryBytes:N0} [byte]";
        string loadTimeStr = $"{slot.Entry.LoadTimeMs} [ms]";
        string colorCountStr = slot.Entry.UniqueColorCount > 0
            ? $"{slot.Entry.UniqueColorCount} 色"
            : "-";

        double scalePct = slot.Entry.OriginalHeight > 0
            ? (double)slot.DisplayedHeight / slot.Entry.OriginalHeight * 100.0
            : 100.0;
        string otherInfo = $"ページ: {slot.PageNumber} / {slot.TotalPages}\r\n表示倍率: {scalePct:F1}%";

        (string Label, string Value)[] rows =
        [
            ("ファイル名", displayFileName),
            ("パス", displayPath),
            ("ファイルサイズ", $"{fileSizeBytes:N0} [byte]"),
            ("更新日時", updateTimeStr),
            ("原寸サイズ", origSizeStr),
            ("フィルタ後サイズ", "-"),
            ("表示サイズ", dispSizeStr),
            ("キャッシュサイズ", cacheSizeStr),
            ("ロード時間", loadTimeStr),
            ("使用色数", colorCountStr)
        ];

        var tableGrid = new Grid
        {
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        tableGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = valueColumnMinWidth
        });

        var labelBgBrush = new SolidColorBrush(Color.FromRgb(196, 196, 196));
        var dashStrokeBrush = new SolidColorBrush(Color.FromRgb(156, 156, 156));
        labelBgBrush.Freeze();
        dashStrokeBrush.Freeze();

        for (int i = 0; i < rows.Length; i++)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });

            // 左カラム (グレー背景 + ラベル + 下部破線)
            var labelCell = new Grid { Background = labelBgBrush };
            var labelText = new TextBlock
            {
                Text = rows[i].Label,
                FontSize = 12,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            labelCell.Children.Add(labelText);
            labelCell.Children.Add(CreateBottomDashedLine(dashStrokeBrush));
            Grid.SetRow(labelCell, i);
            Grid.SetColumn(labelCell, 0);
            tableGrid.Children.Add(labelCell);

            // 右カラム (白背景 + 横リサイズ追従 + 枠から溢れている時のみツールチップ表示 + 下部破線)
            var valueCell = new Grid { Background = Brushes.White };
            string rowText = rows[i].Value;
            var valueBox = new TextBox
            {
                Text = rowText,
                ToolTip = rowText,
                FontSize = 12,
                Foreground = Brushes.Black,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.IBeam
            };

            // 表示域に収まっている場合はマウスオーバーのツールチップを表示しない
            valueBox.ToolTipOpening += (_, e) =>
            {
                if (!IsTextBoxContentOverflowing(valueBox))
                {
                    e.Handled = true;
                }
            };

            labelCell.MouseLeftButtonDown += (_, _) => valueBox.Focus();
            ToolTipService.SetInitialShowDelay(valueBox, 100);
            ToolTipService.SetShowDuration(valueBox, 15000);
            valueCell.Children.Add(valueBox);
            valueCell.Children.Add(CreateBottomDashedLine(dashStrokeBrush));
            Grid.SetRow(valueCell, i);
            Grid.SetColumn(valueCell, 1);
            tableGrid.Children.Add(valueCell);
        }

        // 11行目: 「その他」行 (MangaMeeya準拠のスクロール可能な複数行テキスト枠)
        int otherRowIdx = rows.Length;
        tableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

        var otherLabelCell = new Border
        {
            Background = labelBgBrush,
            BorderBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
        otherLabelCell.Child = new TextBlock
        {
            Text = "その他",
            FontSize = 12,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        Grid.SetRow(otherLabelCell, otherRowIdx);
        Grid.SetColumn(otherLabelCell, 0);
        tableGrid.Children.Add(otherLabelCell);

        var otherTextBox = new TextBox
        {
            Text = otherInfo,
            FontSize = 11.5,
            IsReadOnly = true,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Background = Brushes.White,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(3, 2, 3, 2)
        };
        Grid.SetRow(otherTextBox, otherRowIdx);
        Grid.SetColumn(otherTextBox, 1);
        tableGrid.Children.Add(otherTextBox);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = tableGrid
        };
    }

    private static bool IsTextBoxContentOverflowing(TextBox textBox)
    {
        if (string.IsNullOrEmpty(textBox.Text)) return false;

        if (textBox.ViewportWidth > 0 && textBox.ExtentWidth > 0)
        {
            return textBox.ExtentWidth > textBox.ViewportWidth + 1.0;
        }

        double availableWidth = textBox.ActualWidth;
        if (availableWidth <= 0) return false;

        var dpi = VisualTreeHelper.GetDpi(textBox);
        var formatted = new FormattedText(
            textBox.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
            textBox.FontSize,
            Brushes.Black,
            dpi.PixelsPerDip);

        return formatted.Width > availableWidth - 2.0;
    }

    private static Rectangle CreateBottomDashedLine(Brush strokeBrush)
    {
        return new Rectangle
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Stroke = strokeBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection([4, 2]),
            SnapsToDevicePixels = true
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
