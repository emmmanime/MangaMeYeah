using System.Globalization;
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
    AverageSharpStrong = 356,
    AverageSharpVeryStrong = 357
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

    public static string GetIniFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "MangaMeYeah.ini");
    }

    public static string GetJsonFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "MangaMeYeah.settings.json");
    }

    public static ViewerSettings Load()
    {
        string iniPath = GetIniFilePath();
        if (File.Exists(iniPath))
        {
            try
            {
                var settings = LoadFromIniFile(iniPath);
                if (settings != null)
                {
                    return settings;
                }
            }
            catch
            {
                // Fall back to json if ini parsing failed
            }
        }

        // Migration from existing MangaMeYeah.settings.json if MangaMeYeah.ini does not exist yet
        string jsonPath = GetJsonFilePath();
        if (File.Exists(jsonPath))
        {
            try
            {
                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<ViewerSettings>(json);
                if (loaded != null)
                {
                    // Auto-convert and save to MangaMeYeah.ini immediately
                    loaded.Save();
                    return loaded;
                }
            }
            catch
            {
                // Ignore and fall back to default
            }
        }

        var defaultSettings = new ViewerSettings();
        defaultSettings.TryImportFromMangaMeeyaIni();
        return defaultSettings;
    }

    public void Save()
    {
        try
        {
            string iniPath = GetIniFilePath();
            SaveToIniFile(iniPath);
        }
        catch
        {
            // Ignore write errors
        }
    }

    public void SaveToIniFile(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; MangaMeYeah Configuration File");
        sb.AppendLine("; Encoding: UTF-8 without BOM");
        sb.AppendLine();

        sb.AppendLine("[Config]");
        sb.AppendLine($"PageMode={(int)PageMode}");
        sb.AppendLine($"AutoSpreadEnabled={(AutoSpreadEnabled ? 1 : 0)}");
        sb.AppendLine($"SpreadDirection={(int)SpreadDirection}");
        sb.AppendLine($"ScaleMode={(int)ScaleMode}");
        sb.AppendLine($"ShrinkOnly={(ShrinkOnly ? 1 : 0)}");
        sb.AppendLine($"Filter={(int)Filter}");
        sb.AppendLine($"AutoDetectConnectedImages={(AutoDetectConnectedImages ? 1 : 0)}");
        sb.AppendLine($"SuppressConnectedImagePrompt={(SuppressConnectedImagePrompt ? 1 : 0)}");
        sb.AppendLine($"Sort={(int)FileSort}");
        sb.AppendLine($"FolderSort={(int)FolderSort}");
        sb.AppendLine($"ZoomSpeed={ZoomStepPercent}");
        sb.AppendLine($"DScale={(int)Math.Round(FixedZoomFactor * 100)}");
        sb.AppendLine($"OptionalScale={(int)Math.Round(SpecifiedScaleFactor * 100)}");
        sb.AppendLine($"FilpScaleMode={(ResetScaleOnSameMode ? 1 : 0)}");
        sb.AppendLine($"Distance1={StepNextPrevCount}");
        sb.AppendLine($"Distance2={FixedJumpDistance}");
        sb.AppendLine($"Distance3={PageUpDownCount}");
        sb.AppendLine($"Distance4={FixedRatioJumpCount}");
        sb.AppendLine($"MoveCheckSize={(CheckImageSizeOnMove ? 1 : 0)}");
        sb.AppendLine($"LeftDragScrollSpeed={DragScrollPixel}");
        sb.AppendLine($"KeyBoardHScrollSpeed={KeyScrollStepX}");
        sb.AppendLine($"KeyBoardVScrollSpeed={KeyScrollStepY}");
        sb.AppendLine($"MouseWheelHScrollSpeed={WheelScrollStepX}");
        sb.AppendLine($"MouseWheelVScrollSpeed={WheelScrollStepY}");
        sb.AppendLine($"ScrollUnit={ScrollUnitMode}");
        sb.AppendLine($"SubfolderReadMode={SubfolderReadMode}");
        sb.AppendLine($"IncludeFolderArchivesAsSubfolders={(IncludeFolderArchivesAsSubfolders ? 1 : 0)}");
        sb.AppendLine($"IncludeArchiveArchivesAsSubfolders={(IncludeArchiveArchivesAsSubfolders ? 1 : 0)}");
        sb.AppendLine($"InitialFolderPath={InitialFolderPath}");
        sb.AppendLine($"StartupOpenMode={StartupOpenMode}");
        sb.AppendLine($"AddBlankFirstPage={(AddBlankFirstPage ? 1 : 0)}");
        sb.AppendLine($"DispScrollBar={(ShowScrollBars ? 1 : 0)}");
        sb.AppendLine($"CaptionText={(ShowSubfolderInCaption ? 1 : 0)}");
        sb.AppendLine($"DispTextInfo={(ShowOsdText ? 1 : 0)}");
        sb.AppendLine($"HideCursor={(FullScreenHideCursor ? 1 : 0)}");
        sb.AppendLine($"TopMost={(FullScreenTopMost ? 1 : 0)}");
        sb.AppendLine($"OpenLastFileOnStartup={(OpenLastFileOnStartup ? 1 : 0)}");
        sb.AppendLine($"SaveWindowPosSize={(SaveWindowPlacementOnExit ? 1 : 0)}");
        sb.AppendLine($"PreventMultipleInstances={(PreventMultipleInstances ? 1 : 0)}");
        sb.AppendLine($"FullScreen={(IsFullScreen ? 1 : 0)}");
        sb.AppendLine($"StartUpWidth={SavedWindowWidth.ToString("F0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StartUpHeight={SavedWindowHeight.ToString("F0", CultureInfo.InvariantCulture)}");
        if (SavedWindowLeft.HasValue) sb.AppendLine($"StartUpLeft={SavedWindowLeft.Value.ToString("F1", CultureInfo.InvariantCulture)}");
        if (SavedWindowTop.HasValue) sb.AppendLine($"StartUpTop={SavedWindowTop.Value.ToString("F1", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrEmpty(LastOpenedPath)) sb.AppendLine($"CurrentFolder={LastOpenedPath}");
        sb.AppendLine();

        sb.AppendLine("[Cache]");
        sb.AppendLine($"CacheNum={MaxCachedBitmapCount}");
        sb.AppendLine($"FileCacheSize={FileCacheSizeMb}");
        sb.AppendLine($"ResizeCache={(ResizeCacheEnabled ? 1 : 0)}");
        sb.AppendLine($"PreloadEnabled={(PreloadEnabled ? 1 : 0)}");
        sb.AppendLine($"PrepageForwardNum={PreloadForwardCount}");
        sb.AppendLine($"PrepageBackwardNum={PreloadBackwardCount}");
        sb.AppendLine($"PrepageFileCacheRate={PreloadRatioPercent}");
        sb.AppendLine($"PrepageThreadPriority={PreloadThreadPriority}");
        sb.AppendLine($"GCLimitSize={CacheMemoryLimitMb}");
        sb.AppendLine();

        sb.AppendLine("[Loupe]");
        sb.AppendLine($"Radius={LoupeRadius.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Scale={LoupeScale.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"IsCircle={(LoupeIsCircle ? 1 : 0)}");
        sb.AppendLine($"ShowBorder={(LoupeShowBorder ? 1 : 0)}");
        sb.AppendLine($"KeepWithin={(LoupeClampToScreen ? 1 : 0)}");
        sb.AppendLine($"PreventPop={(SuppressPopupsDuringLoupe ? 1 : 0)}");
        sb.AppendLine();

        sb.AppendLine("[BG]");
        sb.AppendLine($"ColorHex={BackgroundColorHex}");
        sb.AppendLine($"Color={ConvertHexToRgbInt(BackgroundColorHex)}");
        sb.AppendLine();

        sb.AppendLine("[BG2]");
        sb.AppendLine($"ColorHex={BookshelfBackgroundColorHex}");
        sb.AppendLine($"Color={ConvertHexToRgbInt(BookshelfBackgroundColorHex)}");
        sb.AppendLine();

        sb.AppendLine("[Thumbnail]");
        sb.AppendLine($"Columns={CustomThumbnailColumns}");
        sb.AppendLine($"Size={CustomThumbnailSize}");
        sb.AppendLine();

        for (int i = 0; i < 4; i++)
        {
            var slot = (i < QuickSaveSlots.Count) ? QuickSaveSlots[i] : new QuickSaveSlot { SlotNumber = i + 1 };
            sb.AppendLine($"[QuickSave{i + 1}]");
            sb.AppendLine($"Folder={slot.FolderPath}");
            sb.AppendLine($"Option={(slot.PrependFolderName ? 1 : 0)}");
            sb.AppendLine();
        }

        sb.AppendLine("[Resume]");
        foreach (var kvp in ResumePagePerLocation)
        {
            if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            {
                sb.AppendLine($"{kvp.Key}={kvp.Value}");
            }
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
    }

    public static ViewerSettings LoadFromIniFile(string filePath)
    {
        var settings = new ViewerSettings();
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        string currentSection = "";

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();

            if (currentSection.Equals("Config", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "pagemode":
                        if (Enum.TryParse<PageDisplayMode>(val, true, out var pm) || (int.TryParse(val, out int pmi) && Enum.IsDefined(typeof(PageDisplayMode), pmi)))
                        {
                            settings.PageMode = int.TryParse(val, out int pi) ? (PageDisplayMode)pi : pm;
                        }
                        break;
                    case "autospreadenabled":
                    case "autoread":
                        settings.AutoSpreadEnabled = ParseBool(val, settings.AutoSpreadEnabled);
                        break;
                    case "spreaddirection":
                        if (Enum.TryParse<SpreadDirection>(val, true, out var sd) || int.TryParse(val, out int sdi))
                            settings.SpreadDirection = int.TryParse(val, out int sdi2) ? (SpreadDirection)sdi2 : sd;
                        break;
                    case "scalemode":
                        if (Enum.TryParse<ScaleMode>(val, true, out var sm) || int.TryParse(val, out int smi))
                            settings.ScaleMode = int.TryParse(val, out int smi2) ? (ScaleMode)smi2 : sm;
                        break;
                    case "shrinkonly":
                        settings.ShrinkOnly = ParseBool(val, settings.ShrinkOnly);
                        break;
                    case "filter":
                    case "scalefilter":
                        if (int.TryParse(val, out int fval) && Enum.IsDefined(typeof(InterpolationFilter), fval))
                            settings.Filter = (InterpolationFilter)fval;
                        break;
                    case "autodetectconnectedimages":
                        settings.AutoDetectConnectedImages = ParseBool(val, settings.AutoDetectConnectedImages);
                        break;
                    case "suppressconnectedimageprompt":
                        settings.SuppressConnectedImagePrompt = ParseBool(val, settings.SuppressConnectedImagePrompt);
                        break;
                    case "sort":
                    case "filesort":
                        if (int.TryParse(val, out int sortVal) && Enum.IsDefined(typeof(FileSortMode), sortVal))
                            settings.FileSort = (FileSortMode)sortVal;
                        break;
                    case "foldersort":
                        if (int.TryParse(val, out int fldSortVal) && Enum.IsDefined(typeof(FolderSortMode), fldSortVal))
                            settings.FolderSort = (FolderSortMode)fldSortVal;
                        break;
                    case "zoomspeed":
                    case "zoomsteppercent":
                        settings.ZoomStepPercent = ParseInt(val, settings.ZoomStepPercent);
                        break;
                    case "dscale":
                    case "fixedzoomfactor":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double dsc))
                        {
                            settings.FixedZoomFactor = (dsc >= 10.0) ? Math.Clamp(dsc / 100.0, 0.1, 8.0) : Math.Clamp(dsc, 0.1, 8.0);
                        }
                        break;
                    case "optionalscale":
                    case "specifiedscalefactor":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double optSc))
                        {
                            settings.SpecifiedScaleFactor = (optSc >= 10.0) ? Math.Clamp(optSc / 100.0, 0.1, 8.0) : Math.Clamp(optSc, 0.1, 8.0);
                        }
                        break;
                    case "filpscalemode":
                    case "resetscaleonsamemode":
                        settings.ResetScaleOnSameMode = ParseBool(val, settings.ResetScaleOnSameMode);
                        break;
                    case "distance1":
                    case "stepnextprevcount":
                        settings.StepNextPrevCount = ParseInt(val, settings.StepNextPrevCount);
                        break;
                    case "distance2":
                    case "fixedjumpdistance":
                        settings.FixedJumpDistance = ParseInt(val, settings.FixedJumpDistance);
                        break;
                    case "distance3":
                    case "pageupdowncount":
                        settings.PageUpDownCount = ParseInt(val, settings.PageUpDownCount);
                        break;
                    case "distance4":
                    case "fixedratiojumpcount":
                        settings.FixedRatioJumpCount = ParseInt(val, settings.FixedRatioJumpCount);
                        break;
                    case "movechecksize":
                    case "checkimagesizeonmove":
                        settings.CheckImageSizeOnMove = ParseBool(val, settings.CheckImageSizeOnMove);
                        break;
                    case "leftdragscrollspeed":
                    case "dragscrollpixel":
                        settings.DragScrollPixel = ParseInt(val, settings.DragScrollPixel);
                        break;
                    case "keyboardhscrollspeed":
                    case "keyscrollstepx":
                        settings.KeyScrollStepX = ParseInt(val, settings.KeyScrollStepX);
                        break;
                    case "keyboardvscrollspeed":
                    case "keyscrollstepy":
                        settings.KeyScrollStepY = ParseInt(val, settings.KeyScrollStepY);
                        break;
                    case "mousewheelhscrollspeed":
                    case "wheelscrollstepx":
                        settings.WheelScrollStepX = ParseInt(val, settings.WheelScrollStepX);
                        break;
                    case "mousewheelvscrollspeed":
                    case "wheelscrollstepy":
                        settings.WheelScrollStepY = ParseInt(val, settings.WheelScrollStepY);
                        break;
                    case "scrollunit":
                    case "scrollunitmode":
                        settings.ScrollUnitMode = ParseInt(val, settings.ScrollUnitMode);
                        break;
                    case "subfolder":
                    case "subfolderreadmode":
                        settings.SubfolderReadMode = ParseInt(val, settings.SubfolderReadMode);
                        break;
                    case "includefolderarchivesassubfolders":
                        settings.IncludeFolderArchivesAsSubfolders = ParseBool(val, settings.IncludeFolderArchivesAsSubfolders);
                        break;
                    case "includearchivearchivesassubfolders":
                        settings.IncludeArchiveArchivesAsSubfolders = ParseBool(val, settings.IncludeArchiveArchivesAsSubfolders);
                        break;
                    case "initialfolderpath":
                    case "initialbrowsefolder":
                        settings.InitialFolderPath = val;
                        break;
                    case "startupopenmode":
                    case "startupopen":
                        settings.StartupOpenMode = ParseInt(val, settings.StartupOpenMode);
                        break;
                    case "addblankfirstpage":
                    case "doorpage":
                        settings.AddBlankFirstPage = ParseBool(val, settings.AddBlankFirstPage);
                        break;
                    case "dispscrollbar":
                    case "showscrollbars":
                        settings.ShowScrollBars = ParseBool(val, settings.ShowScrollBars);
                        break;
                    case "captiontext":
                    case "showsubfolderincaption":
                        settings.ShowSubfolderInCaption = ParseBool(val, settings.ShowSubfolderInCaption);
                        break;
                    case "disptextinfo":
                    case "showosdtext":
                        settings.ShowOsdText = ParseBool(val, settings.ShowOsdText);
                        break;
                    case "hidecursor":
                    case "fullscreenhidecursor":
                        settings.FullScreenHideCursor = ParseBool(val, settings.FullScreenHideCursor);
                        break;
                    case "topmost":
                    case "fullscreentopmost":
                        settings.FullScreenTopMost = ParseBool(val, settings.FullScreenTopMost);
                        break;
                    case "openlastfileonstartup":
                        settings.OpenLastFileOnStartup = ParseBool(val, settings.OpenLastFileOnStartup);
                        break;
                    case "savewindowpossize":
                    case "savewindowplacementonexit":
                        settings.SaveWindowPlacementOnExit = ParseBool(val, settings.SaveWindowPlacementOnExit);
                        break;
                    case "preventmultipleinstances":
                        settings.PreventMultipleInstances = ParseBool(val, settings.PreventMultipleInstances);
                        break;
                    case "fullscreen":
                    case "isfullscreen":
                        settings.IsFullScreen = ParseBool(val, settings.IsFullScreen);
                        break;
                    case "startupwidth":
                    case "savedwindowwidth":
                        settings.SavedWindowWidth = ParseDouble(val, settings.SavedWindowWidth);
                        break;
                    case "startupheight":
                    case "savedwindowheight":
                        settings.SavedWindowHeight = ParseDouble(val, settings.SavedWindowHeight);
                        break;
                    case "startupleft":
                    case "savedwindowleft":
                        if (!string.IsNullOrWhiteSpace(val)) settings.SavedWindowLeft = ParseDouble(val, 0);
                        break;
                    case "startuptop":
                    case "savedwindowtop":
                        if (!string.IsNullOrWhiteSpace(val)) settings.SavedWindowTop = ParseDouble(val, 0);
                        break;
                    case "currentfolder":
                    case "lastopenedpath":
                        settings.LastOpenedPath = val;
                        break;
                }
            }
            else if (currentSection.Equals("Cache", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "cachenum":
                    case "maxcachedbitmapcount":
                        settings.MaxCachedBitmapCount = ParseInt(val, settings.MaxCachedBitmapCount);
                        break;
                    case "filecachesize":
                    case "filecachesizemb":
                        settings.FileCacheSizeMb = ParseInt(val, settings.FileCacheSizeMb);
                        break;
                    case "resizecache":
                    case "resizecacheenabled":
                        settings.ResizeCacheEnabled = ParseBool(val, settings.ResizeCacheEnabled);
                        break;
                    case "preloadenabled":
                        settings.PreloadEnabled = ParseBool(val, settings.PreloadEnabled);
                        break;
                    case "prepageforwardnum":
                    case "preloadforwardcount":
                        settings.PreloadForwardCount = ParseInt(val, settings.PreloadForwardCount);
                        break;
                    case "prepagebackwardnum":
                    case "preloadbackwardcount":
                        settings.PreloadBackwardCount = ParseInt(val, settings.PreloadBackwardCount);
                        break;
                    case "prepagefilecacherate":
                    case "preloadratiopercent":
                        settings.PreloadRatioPercent = ParseInt(val, settings.PreloadRatioPercent);
                        break;
                    case "prepagethreadpriority":
                    case "preloadthreadpriority":
                        settings.PreloadThreadPriority = Math.Clamp(ParseInt(val, settings.PreloadThreadPriority), 0, 4);
                        break;
                    case "gclimitsize":
                    case "cachememorylimitmb":
                        settings.CacheMemoryLimitMb = ParseInt(val, settings.CacheMemoryLimitMb);
                        break;
                }
            }
            else if (currentSection.Equals("Loupe", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "radius":
                    case "louperadius":
                        settings.LoupeRadius = ParseDouble(val, settings.LoupeRadius);
                        break;
                    case "scale":
                    case "loupescale":
                        settings.LoupeScale = ParseDouble(val, settings.LoupeScale);
                        break;
                    case "iscircle":
                    case "loupeiscircle":
                        settings.LoupeIsCircle = ParseBool(val, settings.LoupeIsCircle);
                        break;
                    case "showborder":
                    case "loupeshowborder":
                        settings.LoupeShowBorder = ParseBool(val, settings.LoupeShowBorder);
                        break;
                    case "keepwithin":
                    case "loupeclamptoscreen":
                        settings.LoupeClampToScreen = ParseBool(val, settings.LoupeClampToScreen);
                        break;
                    case "preventpop":
                    case "suppresspopupsduringloupe":
                        settings.SuppressPopupsDuringLoupe = ParseBool(val, settings.SuppressPopupsDuringLoupe);
                        break;
                }
            }
            else if (currentSection.Equals("BG", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("ColorHex", StringComparison.OrdinalIgnoreCase))
                {
                    settings.BackgroundColorHex = val;
                }
                else if (key.Equals("Color", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.StartsWith('#'))
                        settings.BackgroundColorHex = val;
                    else if (int.TryParse(val, out int rgb))
                        settings.BackgroundColorHex = ConvertRgbIntToHex(rgb);
                }
            }
            else if (currentSection.Equals("BG2", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("ColorHex", StringComparison.OrdinalIgnoreCase))
                {
                    settings.BookshelfBackgroundColorHex = val;
                }
                else if (key.Equals("Color", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.StartsWith('#'))
                        settings.BookshelfBackgroundColorHex = val;
                    else if (int.TryParse(val, out int rgb))
                        settings.BookshelfBackgroundColorHex = ConvertRgbIntToHex(rgb);
                }
            }
            else if (currentSection.Equals("Thumbnail", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "columns":
                    case "customthumbnailcolumns":
                        settings.CustomThumbnailColumns = ParseInt(val, settings.CustomThumbnailColumns);
                        break;
                    case "size":
                    case "customthumbnailsize":
                        settings.CustomThumbnailSize = ParseInt(val, settings.CustomThumbnailSize);
                        break;
                }
            }
            else if (currentSection.StartsWith("QuickSave", StringComparison.OrdinalIgnoreCase))
            {
                string numStr = currentSection.Substring("QuickSave".Length);
                if (int.TryParse(numStr, out int slotNum) && slotNum >= 1 && slotNum <= 4)
                {
                    var targetSlot = settings.QuickSaveSlots[slotNum - 1];
                    if (key.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                    {
                        targetSlot.FolderPath = val;
                    }
                    else if (key.Equals("Option", StringComparison.OrdinalIgnoreCase))
                    {
                        targetSlot.PrependFolderName = ParseBool(val, true);
                    }
                }
            }
            else if (currentSection.Equals("Resume", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                {
                    settings.ResumePagePerLocation[key] = val;
                }
            }
        }

        return settings;
    }

    private static bool ParseBool(string val, bool def)
    {
        val = val.Trim();
        if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (val == "0" || val.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return def;
    }

    private static int ParseInt(string val, int def)
    {
        return int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
    }

    private static double ParseDouble(string val, double def)
    {
        return double.TryParse(val.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : def;
    }

    private static int ConvertHexToRgbInt(string hex)
    {
        try
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexVal))
            {
                int r = (hexVal >> 16) & 0xFF;
                int g = (hexVal >> 8) & 0xFF;
                int b = hexVal & 0xFF;
                return r | (g << 8) | (b << 16);
            }
        }
        catch { }
        return 8421504; // 0x808080
    }

    private static string ConvertRgbIntToHex(int rgb)
    {
        byte r = (byte)(rgb & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)((rgb >> 16) & 0xFF);
        return $"#{r:X2}{g:X2}{b:X2}";
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
