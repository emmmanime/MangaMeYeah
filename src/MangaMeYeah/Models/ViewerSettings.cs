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
    RightToLeft = 0, // 右開き (マンガ標準: 右に若いページ、左に次ページ / 縦並び時は下から上)
    LeftToRight = 1  // 左開き (左に若いページ、右に次ページ / 縦並び時は上から下)
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
    Halftone = 350,
    AveragePixel = 351,
    Lanczos = 352,
    Bicubic = 353,
    Linear = 354,
    AverageSharpLight = 355,
    AverageSharpStrong = 356
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
    PathNaturalDesc = 412,
    PathCharAsc = 413,
    PathCharDesc = 414,
    EntryAsc = 415,
    EntryDesc = 416,
    PathUnicodeAsc = 417,
    PathUnicodeDesc = 418
}

public enum FolderSortMode
{
    ExplorerName = 344,  // エクスプローラ名前順
    NaturalNumber = 345, // 数字順
    Unicode = 346,       // UNICODE順
    Random = 347,        // ランダム
    Size = 348,          // サイズ順
    Date = 349           // 更新日時順
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

    // 連結画像自動検出設定 (アイデアノート準拠)
    public bool AutoDetectConnectedImages { get; set; } = true;
    public bool SuppressConnectedImagePrompt { get; set; } = false;

    // ソート設定
    public FileSortMode FileSort { get; set; } = FileSortMode.PathNaturalAsc;
    public FolderSortMode FolderSort { get; set; } = FolderSortMode.ExplorerName;

    // 操作設定 (Tab 1 - MangaMeeya.ini: ZoomSpeed=5, DScale=125, FilpScaleMode=1)
    public int ZoomStepPercent { get; set; } = 5;
    public double FixedZoomFactor { get; set; } = 1.25;      // MangaMeeya.ini DScale=125 (125%)
    public double SpecifiedScaleFactor { get; set; } = 1.70; // MangaMeeya.ini OptionalScale=170 (170%)
    public bool ResetScaleOnSameMode { get; set; } = true;   // MangaMeeya.ini FilpScaleMode=1

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

    // 一般設定 1 (Tab 2: 読み込み設定)
    public int SubfolderReadMode { get; set; } = 1; // 0=確認ダイアログ, 1=サブフォルダも読み込む, 2=サブフォルダは読み込まない
    public bool IncludeFolderArchivesAsSubfolders { get; set; } = true; // フォルダ内書庫もサブフォルダに含める
    public bool IncludeArchiveArchivesAsSubfolders { get; set; } = true; // 書庫内書庫もサブフォルダに含める
    public string InitialFolderPath { get; set; } = string.Empty;
    public int StartupOpenMode { get; set; } = 1; // 1=先頭から開く, 3=サムネイル表示
    public bool AddBlankFirstPage { get; set; } = false;

    // 一般設定 2 (Tab 3: ルーペ・背景)
    public double LoupeRadius { get; set; } = 140;
    public double LoupeScale { get; set; } = 2.0;
    public bool LoupeIsCircle { get; set; } = false;
    public bool LoupeShowBorder { get; set; } = true;
    public bool LoupeClampToScreen { get; set; } = true;
    public bool SuppressPopupsDuringLoupe { get; set; } = true;
    public string BackgroundColorHex { get; set; } = "#808080";
    public string BookshelfBackgroundColorHex { get; set; } = "#808080";

    // その他設定 (Tab 4)
    public bool ShowScrollBars { get; set; } = false;
    public bool ShowSubfolderInCaption { get; set; } = true;
    public bool ShowOsdText { get; set; } = true;
    public bool FullScreenHideCursor { get; set; } = false;
    public bool FullScreenTopMost { get; set; } = false;
    public bool OpenLastFileOnStartup { get; set; } = false;
    public bool SaveWindowPlacementOnExit { get; set; } = true;
    public bool PreventMultipleInstances { get; set; } = false;
    public bool IsFullScreen { get; set; } = false;
    public double SavedWindowWidth { get; set; } = 1120;
    public double SavedWindowHeight { get; set; } = 800;
    public double? SavedWindowLeft { get; set; }
    public double? SavedWindowTop { get; set; }

    // レジューム記憶: フォルダーや書庫のパス -> 最後に閲覧していたファイル名
    public Dictionary<string, string> ResumePagePerLocation { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // キャッシュ・先読み設定 (Tab 5: デフォルト先読み枚数は4枚以上とし自動連結画像検出に対応)
    public int MaxCachedBitmapCount { get; set; } = 32;
    public int FileCacheSizeMb { get; set; } = 512;
    public bool ResizeCacheEnabled { get; set; } = true;
    public bool PreloadEnabled { get; set; } = true;
    public int PreloadForwardCount { get; set; } = 4;
    public int PreloadBackwardCount { get; set; } = 4;
    public int PreloadRatioPercent { get; set; } = 75; // ファイルキャッシュ先読み割合 (%)
    public int PreloadThreadPriority { get; set; } = 2; // 先読みスレッド優先度: 0=最低, 1=やや低, 2=通常, 3=やや高, 4=最高
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
                else
                {
                    if (line.StartsWith("DScale=") && int.TryParse(line["DScale=".Length..].Trim(), out int dscale))
                    {
                        FixedZoomFactor = Math.Clamp(dscale / 100.0, 0.1, 8.0);
                    }
                    else if (line.StartsWith("OptionalScale=") && int.TryParse(line["OptionalScale=".Length..].Trim(), out int optScale))
                    {
                        SpecifiedScaleFactor = Math.Clamp(optScale / 100.0, 0.1, 8.0);
                    }
                    else if (line.StartsWith("ZoomSpeed=") && int.TryParse(line["ZoomSpeed=".Length..].Trim(), out int zspeed))
                    {
                        ZoomStepPercent = Math.Clamp(zspeed, 1, 200);
                    }
                    else if (line.StartsWith("FilpScaleMode="))
                    {
                        ResetScaleOnSameMode = line["FilpScaleMode=".Length..].Trim() == "1";
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
