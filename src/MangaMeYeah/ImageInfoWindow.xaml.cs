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
    public ImageInfoWindow(IReadOnlyList<DisplayedSlotInfo> slots, int totalRows, int totalColumns)
    {
        InitializeComponent();

        int rows = Math.Max(1, totalRows);
        int cols = Math.Max(1, totalColumns);

        for (int r = 0; r < rows; r++)
        {
            InfoLayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int c = 0; c < cols; c++)
        {
            InfoLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        // 横並びの枚数に応じて値カラムの表示幅を自動調整 (長いファイル名やパスが読みやすいよう拡大)
        double valueColWidth = cols switch
        {
            1 => 380,
            2 => 310,
            3 => 260,
            _ => 220
        };

        // 再帰処理で1枚ずつ配置セルへ振り分けてダイアログを構築
        PopulateSlotGridRecursive(slots, 0, InfoLayoutGrid, valueColWidth);
    }

    private static void PopulateSlotGridRecursive(
        IReadOnlyList<DisplayedSlotInfo> slots,
        int currentIndex,
        Grid targetGrid,
        double valueColumnWidth)
    {
        if (currentIndex >= slots.Count)
        {
            return;
        }

        var slot = slots[currentIndex];
        FrameworkElement cellPanel = CreateSingleImageInfoTable(slot, valueColumnWidth);
        Grid.SetRow(cellPanel, slot.Row);
        Grid.SetColumn(cellPanel, slot.Column);
        targetGrid.Children.Add(cellPanel);

        // 次の枚数を再帰的に配置
        PopulateSlotGridRecursive(slots, currentIndex + 1, targetGrid, valueColumnWidth);
    }

    private static FrameworkElement CreateSingleImageInfoTable(DisplayedSlotInfo slot, double valueColumnWidth)
    {
        var fi = new FileInfo(slot.Entry.FilePath);
        long fileSizeBytes = fi.Exists ? fi.Length : 0;
        string updateTimeStr = fi.Exists
            ? fi.LastWriteTime.ToString("yyyy年M月d日  H:mm:ss")
            : "-";

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
            ("ファイル名", fi.Name),
            ("パス", fi.DirectoryName ?? string.Empty),
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
            Background = Brushes.White
        };
        tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueColumnWidth) });

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

            // 右カラム (白背景 + コピー・横スクロール可能な値 + 即時ツールチップ + 下部破線)
            var valueCell = new Grid { Background = Brushes.White };
            var valueBox = new TextBox
            {
                Text = rows[i].Value,
                ToolTip = rows[i].Value,
                FontSize = 12,
                Foreground = Brushes.Black,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.IBeam
            };
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
            Child = tableGrid
        };
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
        DialogResult = true;
        Close();
    }
}
