using System.IO;
using System.Text;
using System.Text.Json;

namespace MangaMeYeah.Models;

public enum PageDisplayMode
{
    SinglePage = 1,   // 1ページ表示 (1x1)
    TwoPage = 2,      // 横2枚表示・見開き (2列x1行)
    AutoSpread = 3,   // 互換用
    Vertical2 = 4,    // 縦2枚表示 (1列x2行)
    Horizontal3 = 5,  // 横3枚表示 (3列x1行)
    Vertical3 = 6,    // 縦3枚表示 (1列x3行)
    Horizontal4 = 7,  // 横4枚表示 (4列x1行)
    Vertical4 = 8,    // 縦4枚表示 (1列x4行)
    Grid2x2 = 9       // 縦横2×2枚表示 (2列x2行)
}

public enum SpreadDirection
{
    RightToLeft = 0, // 右開き (マンガ標準: 右に若いページ、左に次ページ)
    LeftToRight = 1  // 左開き (左に若いページ、右に次ページ)
}

public enum ScaleMode
{
    FixedScale = 0,         // 倍率固定 (40100)
    FitEither = 1,          // 縦横どちらか合わせる (40101)
    FitWindow = 2,          // ウインドウサイズに合わせる (40102)
    OriginalSize = 3,       // 原寸サイズで表示 (40103)
    FitWindowToImage = 4,   // 画像サイズに合わせる (40104)
    SpecifiedScale = 5,     // 指定スケール、サイズで表示 (40105)
    AlignHeightTwoPage = 6  // 縦サイズを揃える(複数枚表示用) (40106)
}

public enum InterpolationFilter
{
    Halftone = 351,
    AveragePixel = 352,
    Lanczos = 353,
    Bicubic = 354,
    Linear = 355,
    AverageSharpLight = 356,
    AverageSharpStrong = 357
}

public enum FileSortMode
{
    PathAsc = 400,
    PathDesc = 401,
    NameAsc = 402,
    NameDesc = 403,
    ExtensionAsc = 404,
    ExtensionDesc = 405,
    SizeAsc = 406,
    SizeDesc = 407,
    DateAsc = 408,
    DateDesc = 409,
    Random = 410,
    PathNaturalAsc = 411,
    PathNaturalDesc = 412
}

public class QuickSaveSlot
{
    public int SlotNumber { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public bool PrependFolderName { get; set; } = true;
}

public class ViewerSettings
{
    // 表示・見開きモード
    public PageDisplayMode PageMode { get; set; } = PageDisplayMode.TwoPage;
    public bool AutoSpreadEnabled { get; set; } = true;
    public SpreadDirection SpreadDirection { get; set; } = SpreadDirection.RightToLeft;
    public ScaleMode ScaleMode { get; set; } = ScaleMode.AlignHeightTwoPage;
    public bool ShrinkOnly { get; set; } = false;
    public InterpolationFilter Filter { get; set; } = InterpolationFilter.Lanczos;

    // 操作設定 (Tab 1)
    public int ZoomStepPercent { get; set; } = 15;
    public double FixedZoomFactor { get; set; } = 1.0;
    public double SpecifiedScaleFactor { get; set; } = 1.70; // MangaMeeya.ini OptionalScale=170
    public bool ResetScaleOnSameMode { get; set; } = false;

    public int StepNextPrevCount { get; set; } = 1;
    public int PageUpDownCount { get; set; } = 10;
    public int FixedJumpDistance { get; set; } = 5;          // MangaMeeya.ini Distance2=5
    public int FixedRatioJumpCount { get; set; } = 10;
    public bool CheckImageSizeOnMove { get; set; } = true;

    public int DragScrollPixel { get; set; } = 1;
    public int KeyScrollStepX { get; set; } = 25;
    public int KeyScrollStepY { get; set; } = 25;
    public int WheelScrollStepX { get; set; } = 25;
    public int WheelScrollStepY { get; set; } = 25;
    public int ScrollUnitMode { get; set; } = 0; // 0=ウインドウサイズ(%), 1=画像サイズ(%), 2=固定値(pixel)

    // 一般設定 1 (Tab 2)
    public string InitialFolderPath { get; set; } = string.Empty;
    public int StartupOpenMode { get; set; } = 1; // 1=先頭から開く, 3=サムネイル表示
    public bool AddBlankFirstPage { get; set; } = false;

    // 一般設定 2 (Tab 3: ルーペ・背景)
    public double LoupeRadius { get; set; } = 140;
    public double LoupeScale { get; set; } = 2.0;
    public bool LoupeIsCircle { get; set; } = false;
    public bool LoupeShowBorder { get; set; } = true;
    public bool LoupeClampToScreen { get; set; } = true;
    public string BackgroundColorHex { get; set; } = "#808080";

    // その他設定 (Tab 4)
    public bool ShowScrollBars { get; set; } = false;
    public bool ShowSubfolderInCaption { get; set; } = true;
    public bool ShowOsdText { get; set; } = true;
    public bool FullScreenHideCursor { get; set; } = false;
    public bool FullScreenTopMost { get; set; } = false;
    public bool OpenLastFileOnStartup { get; set; } = false;
    public bool SaveWindowPlacementOnExit { get; set; } = true;
    public double SavedWindowWidth { get; set; } = 1120;
    public double SavedWindowHeight { get; set; } = 800;
    public double? SavedWindowLeft { get; set; }
    public double? SavedWindowTop { get; set; }

    // キャッシュ・先読み設定 (Tab 5)
    public int MaxCachedBitmapCount { get; set; } = 32;
    public int FileCacheSizeMb { get; set; } = 512;
    public bool ResizeCacheEnabled { get; set; } = true;
    public bool PreloadEnabled { get; set; } = true;
    public int PreloadForwardCount { get; set; } = 6;
    public int PreloadBackwardCount { get; set; } = 6;
    public int CacheMemoryLimitMb { get; set; } = 2048;

    // サムネイル設定
    public int CustomThumbnailColumns { get; set; } = 6;
    public int CustomThumbnailSize { get; set; } = 220;

    // クイックセーブ 1〜4
    public List<QuickSaveSlot> QuickSaveSlots { get; set; } =
    [
        new QuickSaveSlot { SlotNumber = 1, PrependFolderName = true },
        new QuickSaveSlot { SlotNumber = 2, PrependFolderName = true },
        new QuickSaveSlot { SlotNumber = 3, PrependFolderName = true },
        new QuickSaveSlot { SlotNumber = 4, PrependFolderName = true }
    ];

    public string? LastOpenedPath { get; set; }

    private static string GetSettingsFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "MangaMeYeah.settings.json");
    }

    public static ViewerSettings Load()
    {
        string path = GetSettingsFilePath();
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<ViewerSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
            catch
            {
                // Ignore and fall back to default
            }
        }

        var settings = new ViewerSettings();
        settings.TryImportFromMangaMeeyaIni();
        return settings;
    }

    public void Save()
    {
        try
        {
            string path = GetSettingsFilePath();
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch
        {
            // Ignore write errors
        }
    }

    private void TryImportFromMangaMeeyaIni()
    {
        const string legacyIniPath = @"C:\win32\MangaMeeya_74b\MangaMeeya.ini";
        if (!File.Exists(legacyIniPath))
        {
            return;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var lines = File.ReadAllLines(legacyIniPath, Encoding.GetEncoding(932));
            int currentQuickSaveSlot = 0;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("[QuickSave") && line.EndsWith(']'))
                {
                    string numStr = line.Replace("[QuickSave", "").Replace("]", "");
                    if (int.TryParse(numStr, out int slot) && slot >= 1 && slot <= 4)
                    {
                        currentQuickSaveSlot = slot;
                    }
                    else
                    {
                        currentQuickSaveSlot = 0;
                    }
                    continue;
                }

                if (line.StartsWith('['))
                {
                    currentQuickSaveSlot = 0;
                    continue;
                }

                if (currentQuickSaveSlot >= 1 && currentQuickSaveSlot <= 4)
                {
                    var target = QuickSaveSlots[currentQuickSaveSlot - 1];
                    if (line.StartsWith("Folder="))
                    {
                        target.FolderPath = line["Folder=".Length..].Trim();
                    }
                    else if (line.StartsWith("Option="))
                    {
                        target.PrependFolderName = line["Option=".Length..].Trim() == "1";
                    }
                }
            }
        }
        catch
        {
            // Ignore import failures
        }
    }
}
