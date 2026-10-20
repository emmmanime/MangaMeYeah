using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MangaMeYeah.Models;
using MangaMeYeah.Services;
using Microsoft.Win32;

namespace MangaMeYeah;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ViewerSettings _settings;
    private readonly PageCacheEngine _cacheEngine = new();
    private readonly MouseGestureEngine _gestureEngine = new();

    private List<string> _imageFiles = [];
    private List<CachedPageEntry> _currentDisplayedEntries = [];
    private List<DisplayedSlotInfo> _currentDisplayedSlots = [];
    private int _currentGridRows = 1;
    private int _currentGridCols = 1;
    private string? _currentFolderPath;
    private int _currentIndex;
    private int _lastDisplayedPageCount = 1;
    private FileSortMode _currentSortMode = FileSortMode.PathNaturalAsc;

    // 強制2ページ表示インデックス (幅広画像等でもSpaceやメニューから2ページ表示を強制可能にする)
    private int _forcedTwoPageIndex = -1;

    // 連結画像自動検出の状態管理
    private int _lastEvaluatedConnectedIndex = -1;
    private ConnectedImageGroupResult? _activeConnectedOverride;

    // レンダリング再入・レイアウト無限ループ防止
    private bool _isRendering;
    private double _lastRenderedViewportW;
    private double _lastRenderedViewportH;

    // フルスクリーン状態管理 (0=固定表示, 1=ポップアップ表示[デフォルト], 2=非表示)
    private bool _isFullScreen;
    private int _fullScreenMenuMode = 1;
    private WindowState _savedWindowState;
    private WindowStyle _savedWindowStyle;

    // ルーペ状態管理
    private bool _isLoupeActive;
    private Point _lastMousePosition;

    // マウス入力・長押し・ドラッグ・ジェスチャ状態管理
    private bool _isLeftMouseDown;
    private bool _isRightMouseDown;
    private bool _isLeftDragging;
    private bool _isRightGestureDragging;
    private bool _chordActionTriggered;
    private bool _longPressTriggered;
    private Point _leftMouseDownPos;
    private Point _scrollStartOffset;
    private readonly DispatcherTimer _longPressTimer;
    private MouseButton _longPressCandidateButton;

    // ナビゲーション履歴管理（前へ戻る時に正確に直前の表示グループ・枚数に戻す）
    private readonly Stack<int> _navigationHistory = new();
    private PageDisplayMode _savedMultiPageMode = PageDisplayMode.TwoPage;

    // 左上 OSD タイマー
    private readonly DispatcherTimer _osdTimer;

    // サムネイル一覧管理
    private readonly List<ThumbnailItemViewModel> _thumbnailItems = [];
    private readonly ObservableCollection<ThumbnailRowViewModel> _thumbnailRows = [];
    private CancellationTokenSource? _thumbLoadCts;
    private int _lastClickedThumbIndex = -1;
    private double _thumbnailCardWidth = 200;
    private double _thumbnailCardHeight = 230;

    public double ThumbnailCardWidth
    {
        get => _thumbnailCardWidth;
        set { _thumbnailCardWidth = value; OnPropertyChanged(); }
    }

    public double ThumbnailCardHeight
    {
        get => _thumbnailCardHeight;
        set { _thumbnailCardHeight = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    #region Win32 P/Invoke & マルチモニター全画面境界調整 (WM_GETMINMAXINFO)

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int WM_GETMINMAXINFO = 0x0024;

    public void ActivateFromAnotherInstance(string[] args)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (WindowState == WindowState.Minimized)
        {
            ShowWindowAsync(handle, SW_RESTORE);
            WindowState = WindowState.Normal;
        }

        SetForegroundWindow(handle);
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            string targetPath = args[0].Trim('"');
            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                OpenPath(targetPath);
            }
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            // フルスクリーン表示時 (WindowStyle.None) のみ Windows の -8px 最大化境界枠をモニター実矩形に同期補正。
            // 通常のウィンドウモード（タイトルバーあり）時は Aero Snap や通常最大化の座標計算を Windows ネイティブに委譲。
            if (_isFullScreen || WindowStyle == WindowStyle.None)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var rc = monitorInfo.rcMonitor;
                // Win32 の WM_GETMINMAXINFO において、サブモニター上の ptMaxPosition は
                // 「そのサブモニターの左上を原点(0,0)とした相対座標」を指定する必要があります。
                // 仮想画面絶対座標 (rc.Left) を指定すると、左側モニターで二重にマイナスオフセットされ画面外へ消失します。
                mmi.ptMaxPosition.X = rc.Left - monitorInfo.rcMonitor.Left;
                mmi.ptMaxPosition.Y = rc.Top - monitorInfo.rcMonitor.Top;
                mmi.ptMaxSize.X = Math.Abs(rc.Right - rc.Left);
                mmi.ptMaxSize.Y = Math.Abs(rc.Bottom - rc.Top);
                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            }
        }
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    #endregion

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        SourceInitialized += MainWindow_SourceInitialized;

        _settings = ViewerSettings.Load();
        ThumbnailListBox.ItemsSource = _thumbnailRows;

        if (_settings.SaveWindowPlacementOnExit)
        {
            if (_settings.SavedWindowWidth >= 400) Width = _settings.SavedWindowWidth;
            if (_settings.SavedWindowHeight >= 300) Height = _settings.SavedWindowHeight;
            if (_settings.SavedWindowLeft.HasValue && _settings.SavedWindowTop.HasValue)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _settings.SavedWindowLeft.Value;
                Top = _settings.SavedWindowTop.Value;
            }
        }

        ApplyRuntimePreferences();

        // コンテキストメニューの自動表示を抑止し、MangaMeeya仕様の右クリック動作と両立させる
        ViewerRootGrid.ContextMenuOpening += ViewerRootGrid_ContextMenuOpening;

        // 長押しタイマー（MangaMeeya準拠の500ms判定で1頁/2頁切り替え）
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _longPressTimer.Tick += LongPressTimer_Tick;

        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdStatusBorder.Visibility = Visibility.Collapsed;
        };

        UpdateQuickSaveButtonTooltips();

        // エクスプローラーの右クリック「MangaMeYeahで開く」が未登録なら自動登録 (HKCU)
        if (!ExplorerContextMenuService.IsRegistered())
        {
            ExplorerContextMenuService.Register(includeFolder: true, includeFolderBackground: true, includeFiles: false);
        }

        Loaded += (_, _) =>
        {
            UpdateMenuCheckStates();
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && (File.Exists(args[1]) || Directory.Exists(args[1])))
            {
                OpenPath(args[1]);
            }
            else if (_settings.OpenLastFileOnStartup && !string.IsNullOrEmpty(_settings.LastOpenedPath) &&
                     (File.Exists(_settings.LastOpenedPath) || Directory.Exists(_settings.LastOpenedPath) || _settings.LastOpenedPath.Contains('|')))
            {
                OpenPath(_settings.LastOpenedPath);
            }

            if (_settings.IsFullScreen)
            {
                ToggleFullScreen();
            }
        };

        Closed += (_, _) =>
        {
            SaveCurrentResumeState();
            _settings.IsFullScreen = _isFullScreen;
            _settings.Save();
            _cacheEngine.Dispose();
        };
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        SaveCurrentResumeState();
        _settings.IsFullScreen = _isFullScreen;
        if (_settings.SaveWindowPlacementOnExit && !_isFullScreen && WindowState == WindowState.Normal)
        {
            _settings.SavedWindowWidth = Width;
            _settings.SavedWindowHeight = Height;
            _settings.SavedWindowLeft = Left;
            _settings.SavedWindowTop = Top;
        }
        else if (_settings.SaveWindowPlacementOnExit && _isFullScreen)
        {
            if (RestoreBounds.Width >= 400 && RestoreBounds.Height >= 300)
            {
                _settings.SavedWindowWidth = RestoreBounds.Width;
                _settings.SavedWindowHeight = RestoreBounds.Height;
                _settings.SavedWindowLeft = RestoreBounds.Left;
                _settings.SavedWindowTop = RestoreBounds.Top;
            }
        }
    }

    public void ApplyRuntimePreferences()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_settings.BackgroundColorHex) &&
                ColorConverter.ConvertFromString(_settings.BackgroundColorHex) is Color bgColor)
            {
                var brush = new SolidColorBrush(bgColor);
                brush.Freeze();
                ViewerRootGrid.Background = brush;
                LoupeBorder.Background = brush;
            }
        }
        catch
        {
            // Keep existing background
        }

        var barVis = _settings.ShowScrollBars ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        ImageScrollViewer.HorizontalScrollBarVisibility = barVis;
        ImageScrollViewer.VerticalScrollBarVisibility = barVis;

        if (!_settings.ShowOsdText)
        {
            OsdStatusBorder.Visibility = Visibility.Collapsed;
        }

        if (_isFullScreen)
        {
            Topmost = _settings.FullScreenTopMost;
            ViewerRootGrid.Cursor = _settings.FullScreenHideCursor ? Cursors.None : Cursors.Arrow;
        }
        else
        {
            Topmost = false;
            ViewerRootGrid.Cursor = Cursors.Arrow;
        }

        ApplyLoupeSettings();
        UpdateMenuCheckStates();
        if (_imageFiles.Count > 0)
        {
            RenderCurrentPage();
        }
    }

    #region ファイル・フォルダ・書庫読込と複数枚ページ表示 (1枚 / 横2 / 縦2 / 横3 / 縦3 / 横4 / 縦4 / 2×2)

    private void SaveCurrentResumeState()
    {
        if (!string.IsNullOrEmpty(_currentFolderPath) && _imageFiles.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageFiles.Count)
        {
            _settings.ResumePagePerLocation[_currentFolderPath] = _imageFiles[_currentIndex];
        }
    }

    private int DetermineInitialIndex(string locationKey, bool openAtLastPage)
    {
        if (openAtLastPage && _imageFiles.Count > 0)
        {
            return _imageFiles.Count - 1;
        }

        if (_settings.ResumePagePerLocation.TryGetValue(locationKey, out string? lastFile) && !string.IsNullOrEmpty(lastFile))
        {
            int idx = _imageFiles.FindIndex(f =>
                string.Equals(f, lastFile, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(GetDisplayFileName(f), GetDisplayFileName(lastFile), StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                return idx;
            }
        }

        return 0;
    }

    private bool ResolveSubfolderIncludeDecision(string folderPath)
    {
        switch (_settings.SubfolderReadMode)
        {
            case 1: // サブフォルダも読み込む
                return true;
            case 2: // サブフォルダは読み込まない
                return false;
            case 0: // 確認ダイアログ
            default:
                if (ImageFolderService.HasSubfoldersOrArchives(folderPath, _settings.IncludeFolderArchivesAsSubfolders))
                {
                    var result = MessageBox.Show(
                        this,
                        "サブフォルダ（および書庫）内の画像も読み込みますか？\n\n[はい]: サブフォルダも含める\n[いいえ]: 現在のフォルダのみ",
                        "サブフォルダ読み込み",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    return result == MessageBoxResult.Yes;
                }
                return false;
        }
    }

    public void OpenPath(string path, bool openAtLastPage = false)
    {
        SaveCurrentResumeState();

        if (path.Contains('|'))
        {
            var (arch, entry) = ArchiveImageService.SplitArchiveEntryPath(path);
            if (File.Exists(arch))
            {
                _currentFolderPath = arch;
                _imageFiles = ArchiveImageService.GetImagesInArchive(arch, _currentSortMode, _settings.IncludeArchiveArchivesAsSubfolders);
                int idx = _imageFiles.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
                _currentIndex = idx >= 0 ? idx : DetermineInitialIndex(arch, openAtLastPage);
            }
            else
            {
                return;
            }
        }
        else if (ArchiveImageService.IsArchiveFile(path))
        {
            if (!File.Exists(path)) return;
            _currentFolderPath = path;
            _imageFiles = ArchiveImageService.GetImagesInArchive(path, _currentSortMode, _settings.IncludeArchiveArchivesAsSubfolders);
            _currentIndex = DetermineInitialIndex(path, openAtLastPage);
        }
        else if (File.Exists(path))
        {
            string? dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;

            bool includeSub = ResolveSubfolderIncludeDecision(dir);
            _currentFolderPath = dir;
            _imageFiles = ImageFolderService.GetImagesInFolder(
                dir,
                _currentSortMode,
                includeSubfolders: includeSub,
                includeFolderArchives: _settings.IncludeFolderArchivesAsSubfolders,
                includeArchiveArchives: _settings.IncludeArchiveArchivesAsSubfolders);
            int idx = _imageFiles.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            _currentIndex = idx >= 0 ? idx : DetermineInitialIndex(dir, openAtLastPage);
        }
        else if (Directory.Exists(path))
        {
            bool includeSub = ResolveSubfolderIncludeDecision(path);
            _currentFolderPath = path;
            _imageFiles = ImageFolderService.GetImagesInFolder(
                path,
                _currentSortMode,
                includeSubfolders: includeSub,
                includeFolderArchives: _settings.IncludeFolderArchivesAsSubfolders,
                includeArchiveArchives: _settings.IncludeArchiveArchivesAsSubfolders);
            _currentIndex = DetermineInitialIndex(path, openAtLastPage);
        }
        else
        {
            return;
        }

        _forcedTwoPageIndex = -1;
        _lastEvaluatedConnectedIndex = -1;
        _activeConnectedOverride = null;
        _settings.LastOpenedPath = path;
        _cacheEngine.Clear();
        _navigationHistory.Clear();

        if (_imageFiles.Count == 0)
        {
            _currentDisplayedEntries = [];
            _currentDisplayedSlots = [];
            EmptyStatePanel.Visibility = Visibility.Visible;
            PageCanvasGrid.Children.Clear();
            Title = $"MangaMeYeah - [{Path.GetFileName(_currentFolderPath)}] (画像なし)";
            ShowOsd("対応する画像ファイルが見つかりませんでした");
            UpdateMenuCheckStates();
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        RenderCurrentPage();

        if (_settings.StartupOpenMode == 3 && !openAtLastPage)
        {
            OpenThumbnailView(0, _settings.CustomThumbnailSize);
        }

        _cachedForFolderPath = null;
        UpdateSiblingFolderCache();
    }

    private void EvaluateConnectedImagesForCurrentIndex()
    {
        if (_imageFiles.Count < 2 || _currentIndex < 0 || _currentIndex >= _imageFiles.Count)
        {
            _activeConnectedOverride = null;
            return;
        }

        if (_forcedTwoPageIndex == _currentIndex)
        {
            _activeConnectedOverride = null;
            return;
        }

        if (_lastEvaluatedConnectedIndex == _currentIndex)
        {
            return;
        }

        _lastEvaluatedConnectedIndex = _currentIndex;
        _activeConnectedOverride = null;

        var detected = ConnectedImageDetector.DetectConnectedGroup(_imageFiles, _currentIndex, _cacheEngine, _settings);
        if (detected == null)
        {
            return;
        }

        if (_settings.AutoDetectConnectedImages)
        {
            _activeConnectedOverride = detected;
            ShowOsd($"連結画像を自動検出: {detected.Description}");
        }
        else if (!_settings.SuppressConnectedImagePrompt)
        {
            int promptIdx = _currentIndex;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (promptIdx != _currentIndex || _activeConnectedOverride != null) return;
                var prompt = new ConnectedImagePromptWindow(detected.Description) { Owner = this };
                bool? res = prompt.ShowDialog();
                if (prompt.SuppressFuturePrompts)
                {
                    _settings.SuppressConnectedImagePrompt = true;
                    _settings.Save();
                }
                if (res == true && promptIdx == _currentIndex)
                {
                    _activeConnectedOverride = detected;
                    ShowOsd($"連結表示に切替: {detected.Description}");
                    RenderCurrentPage();
                }
            });
        }
    }

    private (int Rows, int Cols, int Count) ResolveDisplayGridLayout(int startIndex, PageDisplayMode effectiveMode)
    {
        int remaining = Math.Max(0, _imageFiles.Count - startIndex);
        if (remaining == 0) return (1, 1, 0);

        switch (effectiveMode)
        {
            case PageDisplayMode.SinglePage:
                return (1, 1, 1);

            case PageDisplayMode.TwoPage:
            case PageDisplayMode.AutoSpread:
                if (_activeConnectedOverride == null && _forcedTwoPageIndex != startIndex && _settings.AddBlankFirstPage && startIndex == 0)
                {
                    return (1, 1, 1);
                }
                if (!ShouldDisplayTwoPages(startIndex))
                {
                    return (1, 1, 1);
                }
                return (1, 2, Math.Min(2, remaining));

            case PageDisplayMode.Vertical2:
            {
                int count = Math.Min(2, remaining);
                return (count, 1, count);
            }
            case PageDisplayMode.Horizontal3:
            {
                int count = Math.Min(3, remaining);
                return (1, count, count);
            }
            case PageDisplayMode.Vertical3:
            {
                int count = Math.Min(3, remaining);
                return (count, 1, count);
            }
            case PageDisplayMode.Horizontal4:
            {
                int count = Math.Min(4, remaining);
                return (1, count, count);
            }
            case PageDisplayMode.Vertical4:
            {
                int count = Math.Min(4, remaining);
                return (count, 1, count);
            }
            case PageDisplayMode.Grid2x2:
            {
                int count = Math.Min(4, remaining);
                int rows = count > 2 ? 2 : 1;
                int cols = count > 1 ? 2 : 1;
                return (rows, cols, count);
            }
            default:
                return (1, 1, 1);
        }
    }

    private void RenderCurrentPage()
    {
        if (_isRendering) return;
        _isRendering = true;

        try
        {
            if (_imageFiles.Count == 0)
            {
                _currentDisplayedEntries = [];
                _currentDisplayedSlots = [];
                EmptyStatePanel.Visibility = Visibility.Visible;
                PageCanvasGrid.Children.Clear();
                UpdateMenuCheckStates();
                return;
            }

            _currentIndex = Math.Clamp(_currentIndex, 0, _imageFiles.Count - 1);
            if (_forcedTwoPageIndex != _currentIndex)
            {
                _forcedTwoPageIndex = -1;
            }

            EvaluateConnectedImagesForCurrentIndex();

            PageDisplayMode effectiveMode = _activeConnectedOverride?.SuggestedMode ?? _settings.PageMode;
            SpreadDirection effectiveDirection = _activeConnectedOverride?.SuggestedDirection ?? _settings.SpreadDirection;

            double viewportW = Math.Max(320, ViewerRootGrid.ActualWidth);
            double viewportH = Math.Max(240, ViewerRootGrid.ActualHeight);
            _lastRenderedViewportW = viewportW;
            _lastRenderedViewportH = viewportH;

            var (rows, cols, count) = ResolveDisplayGridLayout(_currentIndex, effectiveMode);
            _lastDisplayedPageCount = count;
            _currentGridRows = rows;
            _currentGridCols = cols;

            int decodeTargetW = GetDecodeTargetSize((int)(viewportW / Math.Max(1, cols)), false);
            int decodeTargetH = GetDecodeTargetSize((int)(viewportH / Math.Max(1, rows)), false);

            var entries = new List<CachedPageEntry>(count);
            var indices = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = (_activeConnectedOverride?.PermutedSlotIndices != null &&
                              _activeConnectedOverride.PermutedSlotIndices.Count > i)
                    ? _activeConnectedOverride.PermutedSlotIndices[i]
                    : i;
                int fileIdx = _currentIndex + offset;
                indices.Add(fileIdx);
                entries.Add(_cacheEngine.GetOrLoadPage(_imageFiles[fileIdx], decodeTargetW, decodeTargetH, _settings));
            }
            _currentDisplayedEntries = entries;

            // 各スロットの (Row, Col) と表示サイズを計算
            var slotSizes = CalculateMultiPageSlotSizes(entries, rows, cols, viewportW, viewportH);
            bool rtl = effectiveDirection == SpreadDirection.RightToLeft;

            PageCanvasGrid.Children.Clear();
            PageCanvasGrid.RowDefinitions.Clear();
            PageCanvasGrid.ColumnDefinitions.Clear();

            for (int r = 0; r < rows; r++)
            {
                PageCanvasGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            for (int c = 0; c < cols; c++)
            {
                PageCanvasGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            var slotInfos = new List<DisplayedSlotInfo>(count);
            for (int k = 0; k < count; k++)
            {
                int row;
                int col;
                if (rows == 1)
                {
                    row = 0;
                    col = rtl ? (cols - 1 - k) : k;
                }
                else if (cols == 1)
                {
                    // 縦並び時も左開き(上→下) / 右開き(下→上) の切替で上下の配置順を反転可能にする
                    // ※自動連結検出時は検出された連結順序に従い、手動時は左開き=上→下 / 右開き=下→上または反転に対応
                    bool invertVertical = _activeConnectedOverride != null
                        ? (_activeConnectedOverride.SuggestedDirection == SpreadDirection.RightToLeft)
                        : (_settings.SpreadDirection == SpreadDirection.LeftToRight);
                    row = invertVertical ? (rows - 1 - k) : k;
                    col = 0;
                }
                else
                {
                    // 2×2 グリッド表示
                    row = k / cols;
                    int colInRow = k % cols;
                    col = rtl ? (cols - 1 - colInRow) : colInRow;
                }

                var (dispW, dispH) = slotSizes[k];
                var imgControl = new Image
                {
                    Source = entries[k].Bitmap,
                    Width = dispW,
                    Height = dispH,
                    Stretch = Stretch.Fill,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(imgControl, BitmapScalingMode.HighQuality);

                Grid.SetRow(imgControl, row);
                Grid.SetColumn(imgControl, col);
                PageCanvasGrid.Children.Add(imgControl);

                slotInfos.Add(new DisplayedSlotInfo
                {
                    Row = row,
                    Column = col,
                    PageNumber = indices[k] + 1,
                    TotalPages = _imageFiles.Count,
                    Entry = entries[k],
                    DisplayedWidth = Math.Max(1, (int)Math.Round(dispW)),
                    DisplayedHeight = Math.Max(1, (int)Math.Round(dispH))
                });
            }

            _currentDisplayedSlots = slotInfos;
            UpdateWindowTitle(indices, entries);
            UpdateMenuCheckStates();

            if (_imageInfoWindow != null && _imageInfoWindow.IsLoaded)
            {
                EnsureColorCountsForCurrentSlots();
                _imageInfoWindow.UpdateSlots(_currentDisplayedSlots, _currentGridRows, _currentGridCols);
            }

            ImageScrollViewer.ScrollToTop();
            ImageScrollViewer.ScrollToHorizontalOffset(0);

            if (_isLoupeActive)
            {
                UpdateLoupePosition(_lastMousePosition);
            }

            // 先読み実行
            _cacheEngine.TriggerPreload(_imageFiles, _currentIndex, decodeTargetW, decodeTargetH, _settings);
        }
        finally
        {
            _isRendering = false;
        }
    }

    private List<(double Width, double Height)> CalculateMultiPageSlotSizes(
        IReadOnlyList<CachedPageEntry> entries,
        int rows,
        int cols,
        double viewW,
        double viewH)
    {
        int count = entries.Count;
        var results = new List<(double Width, double Height)>(count);
        if (count == 0) return results;

        if (_settings.ScaleMode == ScaleMode.OriginalSize)
        {
            foreach (var e in entries)
                results.Add((Math.Max(1, e.OriginalWidth), Math.Max(1, e.OriginalHeight)));
            return results;
        }

        if (_settings.ScaleMode is ScaleMode.FixedScale or ScaleMode.SpecifiedScale)
        {
            double factor = _settings.ScaleMode == ScaleMode.FixedScale
                ? _settings.FixedZoomFactor
                : _settings.SpecifiedScaleFactor;
            foreach (var e in entries)
                results.Add((Math.Max(1, e.OriginalWidth * factor), Math.Max(1, e.OriginalHeight * factor)));
            return results;
        }

        if (count == 1)
        {
            var e = entries[0];
            int origW = Math.Max(1, e.OriginalWidth);
            int origH = Math.Max(1, e.OriginalHeight);
            if (_settings.ScaleMode == ScaleMode.FitWindow)
            {
                results.Add((viewW, viewH));
                return results;
            }
            double scale = Math.Min(viewW / origW, viewH / origH);
            if (_settings.ShrinkOnly && scale > 1.0) scale = 1.0;
            results.Add((origW * scale, origH * scale));
            return results;
        }

        // 横並び (横2枚 / 横3枚 / 横4枚): 全画像の縦サイズを揃えて画面内に収める
        if (rows == 1 && cols > 1)
        {
            double sumAspect = 0;
            int minOrigH = int.MaxValue;
            foreach (var e in entries)
            {
                int w = Math.Max(1, e.OriginalWidth);
                int h = Math.Max(1, e.OriginalHeight);
                sumAspect += (double)w / h;
                if (h < minOrigH) minOrigH = h;
            }

            double targetH = Math.Min(viewH, viewW / Math.Max(0.01, sumAspect));
            if (_settings.ShrinkOnly)
            {
                targetH = Math.Min(targetH, minOrigH);
            }

            foreach (var e in entries)
            {
                int w = Math.Max(1, e.OriginalWidth);
                int h = Math.Max(1, e.OriginalHeight);
                double aspect = (double)w / h;
                results.Add((targetH * aspect, targetH));
            }
            return results;
        }

        // 縦並び (縦2枚 / 縦3枚 / 縦4枚): 全画像の横サイズ(幅)を揃えて画面内に収める
        if (cols == 1 && rows > 1)
        {
            double sumInvAspect = 0;
            int minOrigW = int.MaxValue;
            foreach (var e in entries)
            {
                int w = Math.Max(1, e.OriginalWidth);
                int h = Math.Max(1, e.OriginalHeight);
                sumInvAspect += (double)h / w;
                if (w < minOrigW) minOrigW = w;
            }

            double targetW = Math.Min(viewW, viewH / Math.Max(0.01, sumInvAspect));
            if (_settings.ShrinkOnly)
            {
                targetW = Math.Min(targetW, minOrigW);
            }

            foreach (var e in entries)
            {
                int w = Math.Max(1, e.OriginalWidth);
                int h = Math.Max(1, e.OriginalHeight);
                double invAspect = (double)h / w;
                results.Add((targetW, targetW * invAspect));
            }
            return results;
        }

        // 縦横 2×2 枚表示: 上段・下段それぞれで縦サイズを揃え、合計高さが viewH 内に収まるよう計算
        double rowMaxH = viewH / rows;
        for (int r = 0; r < rows; r++)
        {
            int startIdx = r * cols;
            int rowCount = Math.Min(cols, count - startIdx);
            if (rowCount <= 0) break;

            double rowSumAspect = 0;
            int rowMinOrigH = int.MaxValue;
            for (int i = 0; i < rowCount; i++)
            {
                var e = entries[startIdx + i];
                int w = Math.Max(1, e.OriginalWidth);
                int h = Math.Max(1, e.OriginalHeight);
                rowSumAspect += (double)w / h;
                if (h < rowMinOrigH) rowMinOrigH = h;
            }

            double rowH = Math.Min(rowMaxH, viewW / Math.Max(0.01, rowSumAspect));
            if (_settings.ShrinkOnly)
            {
                rowH = Math.Min(rowH, rowMinOrigH);
            }

            for (int i = 0; i < rowCount; i++)
            {
                var e = entries[startIdx + i];
                int w = Math.Max(1, e.OriginalWidth);
                int h = Math.Max(1, e.OriginalHeight);
                results.Add((rowH * ((double)w / h), rowH));
            }
        }

        return results;
    }

    private bool ShouldDisplayTwoPages(int index)
    {
        if (index + 1 >= _imageFiles.Count)
        {
            return false;
        }

        // メニューやスペースキー等で強制的に2ページ表示が指定された場合は幅広画像でも見開き表示する
        if (_forcedTwoPageIndex == index || _activeConnectedOverride != null)
        {
            return true;
        }

        if (_settings.PageMode == PageDisplayMode.SinglePage)
        {
            return false;
        }

        if (_settings.AutoSpreadEnabled || _settings.PageMode == PageDisplayMode.AutoSpread)
        {
            bool firstLandscape = _cacheEngine.IsLandscape(_imageFiles[index]);
            bool secondLandscape = _cacheEngine.IsLandscape(_imageFiles[index + 1]);
            if (firstLandscape || secondLandscape)
            {
                return false;
            }
        }

        // 次の画像 (index + 1) が連結画像の開始位置である場合、現在ページと次ページを見開き結合してしまうと
        // 連結グループが破壊（1枚巻き込まれて残りが欠落・奇数枚化）されてしまうため、現在ページは単ページ表示として
        // 次ページで連結グループが丸ごと揃って表示されるようアライメントを維持する
        if (_settings.AutoDetectConnectedImages && index + 1 < _imageFiles.Count)
        {
            var nextConnected = ConnectedImageDetector.DetectConnectedGroup(_imageFiles, index + 1, _cacheEngine, _settings);
            if (nextConnected != null)
            {
                return false;
            }
        }

        return true;
    }

    private int GetDecodeTargetSize(int viewportDim, bool isHalfWidth)
    {
        if (_settings.ScaleMode is ScaleMode.OriginalSize or ScaleMode.FixedScale or ScaleMode.SpecifiedScale || _isLoupeActive)
        {
            return 0; // フル解像度デコード
        }
        int baseDim = isHalfWidth ? viewportDim / 2 : viewportDim;
        return Math.Max(1024, (int)(baseDim * 1.5));
    }

    private static string GetDisplayFileName(string filePath)
    {
        if (filePath.Contains('|'))
        {
            var (_, entryKey) = ArchiveImageService.SplitArchiveEntryPath(filePath);
            return Path.GetFileName(entryKey);
        }
        return Path.GetFileName(filePath);
    }

    private void UpdateWindowTitle(IReadOnlyList<int> indices, IReadOnlyList<CachedPageEntry> entries)
    {
        string folderName = !string.IsNullOrEmpty(_currentFolderPath)
            ? (_settings.ShowSubfolderInCaption ? _currentFolderPath : Path.GetFileName(_currentFolderPath))
            : "";

        if (indices.Count == 1)
        {
            var e = entries[0];
            Title = $"MangaMeYeah - [{folderName}] ({indices[0] + 1}/{_imageFiles.Count}) {GetDisplayFileName(e.FilePath)} [{e.OriginalWidth}x{e.OriginalHeight}]";
        }
        else if (indices.Count > 1)
        {
            string rangeStr = $"{indices[0] + 1}-{indices[^1] + 1}/{_imageFiles.Count}";
            string names = string.Join(" / ", entries.Select(e => GetDisplayFileName(e.FilePath)));
            Title = $"MangaMeYeah - [{folderName}] ({rangeStr}) {names}";
        }
    }

    #endregion

    #region ページ移動・フォルダ移動・表示切替・フルスクリーンメニュー制御

    private bool _isTransitioningFolder = false;
    private string? _cachedNextSiblingFolder;
    private string? _cachedPrevSiblingFolder;
    private string? _cachedForFolderPath;

    private void UpdateSiblingFolderCache()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || _currentFolderPath == _cachedForFolderPath)
        {
            return;
        }
        string targetPath = _currentFolderPath;
        _cachedForFolderPath = targetPath;
        Task.Run(() =>
        {
            try
            {
                string? next = ImageFolderService.GetSiblingFolder(targetPath, +1, _settings.FolderSort);
                string? prev = ImageFolderService.GetSiblingFolder(targetPath, -1, _settings.FolderSort);
                if (_currentFolderPath == targetPath)
                {
                    _cachedNextSiblingFolder = next;
                    _cachedPrevSiblingFolder = prev;
                }
            }
            catch { }
        });
    }

    public void GoToFirstPage()
    {
        if (_imageFiles.Count > 0)
        {
            _forcedTwoPageIndex = -1;
            _currentIndex = 0;
            RenderCurrentPage();
            ShowOsd("先頭ページ");
        }
    }

    public void GoToLastPage()
    {
        if (_imageFiles.Count > 0)
        {
            _forcedTwoPageIndex = -1;
            _currentIndex = _imageFiles.Count - 1;
            RenderCurrentPage();
            ShowOsd("最終ページ");
        }
    }

    internal void NextPage()
    {
        if (_imageFiles.Count == 0) return;
        int step = Math.Max(1, _lastDisplayedPageCount * Math.Max(1, _settings.StepNextPrevCount));
        if (_currentIndex + step < _imageFiles.Count)
        {
            _navigationHistory.Push(_currentIndex);
            _forcedTwoPageIndex = -1;
            _currentIndex += step;
            RenderCurrentPage();
        }
        else
        {
            if (_isTransitioningFolder) return;
            _isTransitioningFolder = true;

            try
            {
                string? nextFolder = _cachedNextSiblingFolder ?? (!string.IsNullOrEmpty(_currentFolderPath)
                    ? ImageFolderService.GetSiblingFolder(_currentFolderPath, +1, _settings.FolderSort)
                    : null);

                if (nextFolder != null)
                {
                    ShowOsd($"次のフォルダへ移動中: {Path.GetFileName(nextFolder)}");
                    Mouse.OverrideCursor = Cursors.Wait;
                    OpenPath(nextFolder, openAtLastPage: false);
                    ShowOsd($"フォルダ移動: {Path.GetFileName(nextFolder)}");
                }
                else
                {
                    ShowOsd("最終ページです (次のフォルダはありません)");
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isTransitioningFolder = false;
            }
        }
    }

    internal void PrevPage()
    {
        if (_imageFiles.Count == 0) return;
        if (_currentIndex <= 0)
        {
            if (_isTransitioningFolder) return;
            _isTransitioningFolder = true;

            try
            {
                string? prevFolder = _cachedPrevSiblingFolder ?? (!string.IsNullOrEmpty(_currentFolderPath)
                    ? ImageFolderService.GetSiblingFolder(_currentFolderPath, -1, _settings.FolderSort)
                    : null);

                if (prevFolder != null)
                {
                    ShowOsd($"前のフォルダへ移動中: {Path.GetFileName(prevFolder)}");
                    Mouse.OverrideCursor = Cursors.Wait;
                    OpenPath(prevFolder, openAtLastPage: true);
                    ShowOsd($"フォルダ移動: {Path.GetFileName(prevFolder)}");
                }
                else
                {
                    ShowOsd("先頭ページです (前のフォルダはありません)");
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isTransitioningFolder = false;
            }
            return;
        }

        _forcedTwoPageIndex = -1;

        // 1. 直前のナビゲーション履歴があれば正確に元の開始インデックスに戻す (4枚連結などの逆順復元)
        if (_navigationHistory.Count > 0)
        {
            int prevIdx = _navigationHistory.Pop();
            if (prevIdx < _currentIndex && prevIdx >= 0)
            {
                _currentIndex = prevIdx;
                RenderCurrentPage();
                return;
            }
        }

        // 2. 履歴がない場合（直接ジャンプ等）、手前の画像が4枚・3枚・2枚連結グループか後方判定
        int candidateStep = 0;
        if (_currentIndex >= 4)
        {
            var g4 = ConnectedImageDetector.DetectConnectedGroup(_imageFiles, _currentIndex - 4, _cacheEngine, _settings);
            if (g4 != null && g4.ImageCount == 4) candidateStep = 4;
        }
        if (candidateStep == 0 && _currentIndex >= 3)
        {
            var g3 = ConnectedImageDetector.DetectConnectedGroup(_imageFiles, _currentIndex - 3, _cacheEngine, _settings);
            if (g3 != null && g3.ImageCount == 3) candidateStep = 3;
        }
        if (candidateStep == 0 && _currentIndex >= 2)
        {
            var g2 = ConnectedImageDetector.DetectConnectedGroup(_imageFiles, _currentIndex - 2, _cacheEngine, _settings);
            if (g2 != null && g2.ImageCount == 2) candidateStep = 2;
        }

        if (candidateStep > 0)
        {
            _currentIndex = Math.Max(0, _currentIndex - candidateStep);
        }
        else if (_settings.PageMode is PageDisplayMode.TwoPage or PageDisplayMode.AutoSpread)
        {
            int step = 2 * Math.Max(1, _settings.StepNextPrevCount);
            int candidate = Math.Max(0, _currentIndex - step);
            if (step == 2 && candidate < _currentIndex - 1 && !ShouldDisplayTwoPages(candidate))
            {
                candidate = _currentIndex - 1;
            }
            _currentIndex = candidate;
        }
        else
        {
            int baseCount = _settings.PageMode switch
            {
                PageDisplayMode.SinglePage => 1,
                PageDisplayMode.Vertical2 => 2,
                PageDisplayMode.Horizontal3 or PageDisplayMode.Vertical3 => 3,
                PageDisplayMode.Horizontal4 or PageDisplayMode.Vertical4 or PageDisplayMode.Grid2x2 => 4,
                _ => 1
            };
            int step = baseCount * Math.Max(1, _settings.StepNextPrevCount);
            _currentIndex = Math.Max(0, _currentIndex - step);
        }
        RenderCurrentPage();
    }

    private void StepPage(int delta)
    {
        if (_imageFiles.Count == 0) return;
        int next = Math.Clamp(_currentIndex + delta, 0, _imageFiles.Count - 1);
        if (next != _currentIndex)
        {
            _navigationHistory.Push(_currentIndex);
            _forcedTwoPageIndex = -1;
            _currentIndex = next;
            RenderCurrentPage();
        }
    }

    private void MoveToSiblingFolder(int offset)
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || _isTransitioningFolder) return;
        _isTransitioningFolder = true;
        try
        {
            string? targetFolder = offset > 0
                ? (_cachedNextSiblingFolder ?? ImageFolderService.GetSiblingFolder(_currentFolderPath, +1, _settings.FolderSort))
                : (_cachedPrevSiblingFolder ?? ImageFolderService.GetSiblingFolder(_currentFolderPath, -1, _settings.FolderSort));

            if (targetFolder != null)
            {
                ShowOsd(offset > 0 ? $"次のフォルダへ移動中: {Path.GetFileName(targetFolder)}" : $"前のフォルダへ移動中: {Path.GetFileName(targetFolder)}");
                Mouse.OverrideCursor = Cursors.Wait;
                OpenPath(targetFolder, openAtLastPage: offset < 0);
                ShowOsd($"フォルダ移動: {Path.GetFileName(targetFolder)}");
            }
            else
            {
                ShowOsd(offset > 0 ? "次のフォルダはありません" : "前のフォルダはありません");
            }
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _isTransitioningFolder = false;
        }
    }

    private void SetPageDisplayMode(PageDisplayMode mode, string label, bool forceTwoPageAtCurrent = false)
    {
        _settings.PageMode = mode;
        _activeConnectedOverride = null;
        _forcedTwoPageIndex = forceTwoPageAtCurrent ? _currentIndex : -1;
        ShowOsd(label);
        RenderCurrentPage();
    }

    private void TogglePageMode()
    {
        _activeConnectedOverride = null;
        if (_settings.PageMode == PageDisplayMode.SinglePage || _lastDisplayedPageCount == 1)
        {
            // 現在1ページ表示中 → 直前の複数枚モード（Vertical2、TwoPage等）へ復帰
            _settings.PageMode = _savedMultiPageMode != PageDisplayMode.SinglePage
                ? _savedMultiPageMode
                : PageDisplayMode.TwoPage;
            _forcedTwoPageIndex = _currentIndex;
            ShowOsd($"{GetPageModeDisplayName(_settings.PageMode)} (位置調整完了)");
        }
        else
        {
            // 現在複数枚表示中（縦2枚、見開き2枚、4枚など）→ 現在のモードを記憶し、1枚ずれ調整用の1ページ表示へ切替
            _savedMultiPageMode = _settings.PageMode;
            _forcedTwoPageIndex = -1;
            _settings.PageMode = PageDisplayMode.SinglePage;
            ShowOsd("1ページ表示 (1枚ずれ調整用: 次へ進んで再切替)");
        }
        RenderCurrentPage();
    }

    private static string GetPageModeDisplayName(PageDisplayMode mode) => mode switch
    {
        PageDisplayMode.SinglePage => "1ページ表示",
        PageDisplayMode.TwoPage => "横2枚見開き表示",
        PageDisplayMode.AutoSpread => "自動見開き表示",
        PageDisplayMode.Vertical2 => "縦2枚表示",
        PageDisplayMode.Horizontal3 => "横3枚表示",
        PageDisplayMode.Vertical3 => "縦3枚表示",
        PageDisplayMode.Horizontal4 => "横4枚表示",
        PageDisplayMode.Vertical4 => "縦4枚表示",
        PageDisplayMode.Grid2x2 => "縦横2×2枚表示",
        _ => "複数枚表示"
    };

    private void ToggleFullScreen()
    {
        _isFullScreen = !_isFullScreen;
        _settings.IsFullScreen = _isFullScreen;
        if (_isFullScreen)
        {
            _savedWindowState = WindowState;
            _savedWindowStyle = WindowStyle;
            ResizeMode = ResizeMode.NoResize;
            ViewerRootGrid.Margin = new Thickness(0);
            MainMenuBar.Visibility = _fullScreenMenuMode == 0 ? Visibility.Visible : Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ShowOsd("フルスクリーン表示 (画面上端にマウス移動でメニュー表示 / ↑ジェスチャで解除)");
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
            ViewerRootGrid.Margin = new Thickness(0, 24, 0, 0);
            MainMenuBar.Visibility = Visibility.Visible;
            WindowStyle = _savedWindowStyle;
            WindowState = _savedWindowState;
            ShowOsd("ウィンドウ表示");
        }
        UpdateMenuCheckStates();
        Dispatcher.BeginInvoke(DispatcherPriority.Render, RenderCurrentPage);
    }

    private void UpdateFullScreenMenuPopup(Point mousePosInRoot)
    {
        if (!_isFullScreen) return;

        // ルーペ使用中は誤動作防止のためメニュー/ツールバーのポップアップ表示を抑止 (環境設定連動)
        if (_settings.SuppressPopupsDuringLoupe && _isLoupeActive)
        {
            if (MainMenuBar.Visibility == Visibility.Visible && _fullScreenMenuMode == 1)
            {
                MainMenuBar.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (_fullScreenMenuMode == 0)
        {
            MainMenuBar.Visibility = Visibility.Visible;
            return;
        }
        if (_fullScreenMenuMode == 2)
        {
            MainMenuBar.Visibility = Visibility.Collapsed;
            return;
        }

        // ポップアップ表示モード: 画面最上部 (Y <= 16px) にマウスを移動するとプルダウン表示
        if (mousePosInRoot.Y <= 16)
        {
            if (MainMenuBar.Visibility != Visibility.Visible)
            {
                MainMenuBar.Visibility = Visibility.Visible;
            }
        }
        else if (mousePosInRoot.Y > 36 && !MainMenuBar.IsKeyboardFocusWithin && !MainMenuBar.IsMouseOver)
        {
            if (MainMenuBar.Visibility == Visibility.Visible)
            {
                MainMenuBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void MainMenuBar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isFullScreen && _fullScreenMenuMode == 1 && !MainMenuBar.IsKeyboardFocusWithin)
        {
            Point pos = e.GetPosition(ViewerRootGrid);
            if (pos.Y > 28)
            {
                MainMenuBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ToggleAlignHeightTwoPageAndFixedScale()
    {
        if (_settings.ScaleMode == ScaleMode.AlignHeightTwoPage)
        {
            _settings.ScaleMode = ScaleMode.FixedScale;
            int pct = (int)Math.Round(_settings.FixedZoomFactor * 100);
            ShowOsd($"スケール: 倍率固定(デフォルト) ({pct}%)");
        }
        else
        {
            _settings.ScaleMode = ScaleMode.AlignHeightTwoPage;
            ShowOsd("スケール: 縦サイズを揃える(2ページ表示用)");
        }
        RenderCurrentPage();
    }

    private void SetScaleMode(ScaleMode mode, string label)
    {
        if (_settings.ResetScaleOnSameMode && _settings.ScaleMode == mode)
        {
            if (mode == ScaleMode.FixedScale)
            {
                _settings.ScaleMode = ScaleMode.AlignHeightTwoPage;
                ShowOsd("スケール: 縦サイズを揃える(2ページ表示用)");
            }
            else
            {
                _settings.ScaleMode = ScaleMode.FixedScale;
                int pct = (int)Math.Round(_settings.FixedZoomFactor * 100);
                ShowOsd($"スケール: 倍率固定(デフォルト) ({pct}%)");
            }
            RenderCurrentPage();
            return;
        }
        _settings.ScaleMode = mode;
        ShowOsd($"スケール: {label}");
        RenderCurrentPage();
    }

    private void AdjustZoom(double delta)
    {
        double step = Math.Sign(delta) * (_settings.ZoomStepPercent / 100.0);
        if (Math.Abs(step) < 0.01) step = delta;
        _settings.ScaleMode = ScaleMode.FixedScale;
        _settings.FixedZoomFactor = Math.Clamp(_settings.FixedZoomFactor + step, 0.1, 8.0);
        ShowOsd($"倍率固定: {(int)Math.Round(_settings.FixedZoomFactor * 100)}%");
        RenderCurrentPage();
    }

    #endregion

    #region ルーペ機能

    private void ToggleLoupe()
    {
        _isLoupeActive = !_isLoupeActive;
        LoupeOverlayCanvas.Visibility = _isLoupeActive ? Visibility.Visible : Visibility.Collapsed;
        if (_isLoupeActive)
        {
            if (_settings.SuppressPopupsDuringLoupe && _isFullScreen && _fullScreenMenuMode == 1)
            {
                MainMenuBar.Visibility = Visibility.Collapsed;
            }
            ApplyLoupeSettings();
            UpdateLoupePosition(Mouse.GetPosition(ViewerRootGrid));
            ShowOsd($"ルーペ ON ({(int)(_settings.LoupeScale * 100)}%) - 右ボタン+ホイールで倍率変更");
        }
        else
        {
            ShowOsd("ルーペ OFF");
        }
    }

    private void ApplyLoupeSettings()
    {
        double diameter = _settings.LoupeRadius * 2;
        LoupeBorder.Width = diameter;
        LoupeBorder.Height = diameter;
        LoupeBorder.BorderThickness = _settings.LoupeShowBorder ? new Thickness(2) : new Thickness(0);
        LoupeBorder.CornerRadius = _settings.LoupeIsCircle ? new CornerRadius(diameter / 2) : new CornerRadius(4);
        LoupeViewportRect.Width = diameter;
        LoupeViewportRect.Height = diameter;

        try
        {
            if (!string.IsNullOrWhiteSpace(_settings.BackgroundColorHex) &&
                ColorConverter.ConvertFromString(_settings.BackgroundColorHex) is Color bgColor)
            {
                var brush = new SolidColorBrush(bgColor);
                brush.Freeze();
                LoupeBorder.Background = brush;
            }
        }
        catch
        {
            // Keep current background
        }
    }

    private void UpdateLoupePosition(Point mousePosInRoot)
    {
        if (!_isLoupeActive) return;

        double diameter = _settings.LoupeRadius * 2;
        double left = mousePosInRoot.X - _settings.LoupeRadius;
        double top = mousePosInRoot.Y - _settings.LoupeRadius;

        if (_settings.LoupeClampToScreen)
        {
            left = Math.Clamp(left, 0, Math.Max(0, ViewerRootGrid.ActualWidth - diameter));
            top = Math.Clamp(top, 0, Math.Max(0, ViewerRootGrid.ActualHeight - diameter));
        }

        Canvas.SetLeft(LoupeBorder, left);
        Canvas.SetTop(LoupeBorder, top);

        Point posInCanvas = ViewerRootGrid.TranslatePoint(mousePosInRoot, PageCanvasGrid);
        double viewBoxW = diameter / _settings.LoupeScale;
        double viewBoxH = diameter / _settings.LoupeScale;
        LoupeVisualBrush.Viewbox = new Rect(
            posInCanvas.X - viewBoxW / 2,
            posInCanvas.Y - viewBoxH / 2,
            viewBoxW,
            viewBoxH);
    }

    #endregion

    #region クイックセーブ & サムネイル一覧 (複数選択コレクション)

    private void TriggerQuickSave(int slotNumber)
    {
        if (_imageFiles.Count == 0)
        {
            ShowOsd("画像が開かれていません");
            return;
        }

        var slot = _settings.QuickSaveSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
        if (slot == null || string.IsNullOrWhiteSpace(slot.FolderPath))
        {
            ShowOsd($"クイックセーブ{slotNumber} の保存先フォルダを設定してください");
            OpenQuickSaveSettingsDialog();
            slot = _settings.QuickSaveSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
            if (slot == null || string.IsNullOrWhiteSpace(slot.FolderPath))
            {
                return;
            }
        }

        var targets = new List<string>();
        for (int i = 0; i < _lastDisplayedPageCount && _currentIndex + i < _imageFiles.Count; i++)
        {
            targets.Add(_imageFiles[_currentIndex + i]);
        }

        var (_, message) = QuickSaveService.ExecuteQuickSave(slotNumber, targets, _settings);
        ShowOsd(message);
    }

    private void OpenQuickSaveSettingsDialog()
    {
        var dlg = new QuickSaveSettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            UpdateQuickSaveButtonTooltips();
            ShowOsd("クイックセーブ設定を保存しました");
        }
    }

    private void OpenRegistrySettingsDialog()
    {
        var dlg = new RegistrySettingsWindow { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.StatusMessage))
        {
            ShowOsd(dlg.StatusMessage);
        }
    }

    private void OpenPreferencesDialog()
    {
        var dlg = new PreferencesWindow(_settings, ApplyRuntimePreferences) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            ShowOsd("環境設定を保存しました");
        }
    }

    private void UpdateQuickSaveButtonTooltips()
    {
        Button[] buttons = [BatchQs1Button, BatchQs2Button, BatchQs3Button, BatchQs4Button];
        for (int i = 0; i < buttons.Length && i < _settings.QuickSaveSlots.Count; i++)
        {
            string folder = _settings.QuickSaveSlots[i].FolderPath;
            string shortName = !string.IsNullOrEmpty(folder) ? Path.GetFileName(folder) : "未設定";
            buttons[i].Content = $"QS{i + 1}: {shortName}";
            buttons[i].ToolTip = !string.IsNullOrEmpty(folder) ? folder : "クリックして保存先を設定";
        }
    }

    private void OpenThumbnailView(int targetColumns, int thumbSize)
    {
        if (_imageFiles.Count == 0)
        {
            ShowOsd("画像が開かれていません");
            return;
        }

        _thumbLoadCts?.Cancel();
        _thumbLoadCts = new CancellationTokenSource();
        var token = _thumbLoadCts.Token;

        if (targetColumns > 0)
        {
            double availW = Math.Max(600, ViewerRootGrid.ActualWidth - 60);
            int computed = (int)(availW / targetColumns) - 16;
            thumbSize = Math.Clamp(computed, 100, 400);
        }

        ThumbSizeSlider.Value = thumbSize;
        ApplyThumbnailCardSize(thumbSize);

        bool needsRebuild = _thumbnailItems.Count != _imageFiles.Count;
        if (!needsRebuild)
        {
            for (int i = 0; i < _thumbnailItems.Count; i++)
            {
                if (_thumbnailItems[i].FilePath != _imageFiles[i])
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (needsRebuild)
        {
            _thumbnailItems.Clear();
            for (int i = 0; i < _imageFiles.Count; i++)
            {
                var item = new ThumbnailItemViewModel
                {
                    PageIndex = i,
                    FilePath = _imageFiles[i],
                    IsCurrentPage = i == _currentIndex
                };
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ThumbnailItemViewModel.IsSelected))
                    {
                        UpdateThumbnailSummaryText();
                    }
                };
                _thumbnailItems.Add(item);
            }
        }
        else
        {
            for (int i = 0; i < _thumbnailItems.Count; i++)
            {
                _thumbnailItems[i].IsCurrentPage = (i == _currentIndex);
            }
        }

        RebuildThumbnailRows(thumbSize);
        UpdateThumbnailSummaryText();
        ThumbnailOverlayGrid.Visibility = Visibility.Visible;

        // 現在表示中の画像がある行へスクロール
        int cols = CalculateThumbnailColumns(thumbSize);
        int targetRow = _currentIndex / cols;
        if (targetRow >= 0 && targetRow < _thumbnailRows.Count)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                ThumbnailListBox.ScrollIntoView(_thumbnailRows[targetRow]);
            });
        }

        int decodeSize = Math.Max(240, thumbSize);
        // 現在ページ周辺から優先してバックグラウンド生成
        var prioritizedItems = _thumbnailItems
            .OrderBy(item => Math.Abs(item.PageIndex - _currentIndex))
            .ToList();

        Task.Run(async () =>
        {
            foreach (var item in prioritizedItems)
            {
                if (token.IsCancellationRequested) break;
                if (item.ThumbnailBitmap != null) continue;

                var bmp = await _cacheEngine.LoadThumbnailAsync(item.FilePath, decodeSize, token);
                if (bmp != null && !token.IsCancellationRequested)
                {
                    _ = Dispatcher.InvokeAsync(() => item.ThumbnailBitmap = bmp, DispatcherPriority.Background);
                }
            }
        }, token);
    }

    private int CalculateThumbnailColumns(double cardWidth)
    {
        double availW = Math.Max(300, ViewerRootGrid.ActualWidth - 40);
        int cols = (int)(availW / (cardWidth + 16));
        return Math.Max(1, cols);
    }

    private void RebuildThumbnailRows(double cardWidth)
    {
        int cols = CalculateThumbnailColumns(cardWidth);
        _thumbnailRows.Clear();

        for (int i = 0; i < _thumbnailItems.Count; i += cols)
        {
            var row = new ThumbnailRowViewModel();
            for (int j = i; j < i + cols && j < _thumbnailItems.Count; j++)
            {
                row.Items.Add(_thumbnailItems[j]);
            }
            _thumbnailRows.Add(row);
        }
    }

    private void ApplyThumbnailCardSize(double size)
    {
        ThumbnailCardWidth = size;
        ThumbnailCardHeight = size + 30;
    }

    private void UpdateThumbnailSummaryText()
    {
        int selectedCount = _thumbnailItems.Count(t => t.IsSelected);
        ThumbSummaryText.Text = $"サムネイル一覧 (全 {_thumbnailItems.Count} 枚 / 選択中: {selectedCount} 枚)  ※Ctrl/Shift+クリックで複数選択、ダブルクリックで表示";
    }

    private void CloseThumbnailView()
    {
        _thumbLoadCts?.Cancel();
        ThumbnailOverlayGrid.Visibility = Visibility.Collapsed;
    }

    private void ExecuteBatchThumbnailQuickSave(int slotNumber)
    {
        var selectedPaths = _thumbnailItems
            .Where(t => t.IsSelected)
            .Select(t => t.FilePath)
            .ToList();

        if (selectedPaths.Count == 0)
        {
            MessageBox.Show(
                this,
                "クイックセーブするサムネイルにチェックを入れてください（Ctrl+クリック、Shift+クリック、または左上のチェックボックスで複数選択できます）。",
                "複数選択クイックセーブ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var slot = _settings.QuickSaveSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
        if (slot == null || string.IsNullOrWhiteSpace(slot.FolderPath))
        {
            OpenQuickSaveSettingsDialog();
        }

        var (_, message) = QuickSaveService.ExecuteQuickSave(slotNumber, selectedPaths, _settings);
        ShowOsd(message);
    }

    private void ThumbnailCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ThumbnailItemViewModel item)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _currentIndex = item.PageIndex;
            CloseThumbnailView();
            RenderCurrentPage();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _lastClickedThumbIndex >= 0)
        {
            int start = Math.Min(_lastClickedThumbIndex, item.PageIndex);
            int end = Math.Max(_lastClickedThumbIndex, item.PageIndex);
            for (int i = start; i <= end && i < _thumbnailItems.Count; i++)
            {
                _thumbnailItems[i].IsSelected = true;
            }
        }
        else
        {
            item.IsSelected = !item.IsSelected;
        }

        _lastClickedThumbIndex = item.PageIndex;
        e.Handled = true;
    }

    private void ThumbnailCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateThumbnailSummaryText();
        e.Handled = true;
    }

    private void ThumbSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _thumbnailItems) item.IsSelected = true;
    }

    private void ThumbClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _thumbnailItems) item.IsSelected = false;
    }

    private void BatchQuickSave1_Click(object sender, RoutedEventArgs e) => ExecuteBatchThumbnailQuickSave(1);
    private void BatchQuickSave2_Click(object sender, RoutedEventArgs e) => ExecuteBatchThumbnailQuickSave(2);
    private void BatchQuickSave3_Click(object sender, RoutedEventArgs e) => ExecuteBatchThumbnailQuickSave(3);
    private void BatchQuickSave4_Click(object sender, RoutedEventArgs e) => ExecuteBatchThumbnailQuickSave(4);
    private void OpenQuickSaveSettings_Click(object sender, RoutedEventArgs e) => OpenQuickSaveSettingsDialog();
    private void CloseThumbnailView_Click(object sender, RoutedEventArgs e) => CloseThumbnailView();

    private void ThumbSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            ApplyThumbnailCardSize(e.NewValue);
            if (ThumbnailOverlayGrid.Visibility == Visibility.Visible && _thumbnailItems.Count > 0)
            {
                RebuildThumbnailRows(e.NewValue);
            }
        }
    }

    #endregion

    #region MangaMeeya マウス入力＆マウスジェスチャ処理

    private void ViewerRootGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_imageFiles.Count > 0 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
        }
    }

    private void ViewerRootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ThumbnailOverlayGrid.Visibility == Visibility.Visible) return;

        ViewerRootGrid.Focus();
        Point pos = e.GetPosition(ViewerRootGrid);
        _lastMousePosition = pos;

        if (e.ChangedButton == MouseButton.XButton1)
        {
            MoveToSiblingFolder(+1);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.XButton2)
        {
            MoveToSiblingFolder(-1);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            if (_isRightMouseDown)
            {
                _chordActionTriggered = true;
                _longPressTimer.Stop();
                ViewerContextMenu.PlacementTarget = ViewerRootGrid;
                ViewerContextMenu.IsOpen = true;
            }
            else
            {
                ToggleLoupe();
            }
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _isLeftMouseDown = true;
            _leftMouseDownPos = pos;
            _scrollStartOffset = new Point(ImageScrollViewer.HorizontalOffset, ImageScrollViewer.VerticalOffset);

            if (_isRightMouseDown)
            {
                _chordActionTriggered = true;
                _longPressTimer.Stop();
                StepPage(+1);
                ShowOsd("1ページ進む");
                e.Handled = true;
                return;
            }

            _longPressTriggered = false;
            _longPressCandidateButton = MouseButton.Left;
            _longPressTimer.Start();
            ViewerRootGrid.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            _isRightMouseDown = true;

            if (_isLeftMouseDown)
            {
                _chordActionTriggered = true;
                _longPressTimer.Stop();
                StepPage(-1);
                ShowOsd("1ページ戻る");
                e.Handled = true;
                return;
            }

            _gestureEngine.Start(pos);
            GestureTrailPolyline.Points.Clear();
            GestureTrailPolyline.Points.Add(pos);
            GestureStatusText.Text = string.Empty;

            _longPressTriggered = false;
            _longPressCandidateButton = MouseButton.Right;
            _longPressTimer.Start();
            ViewerRootGrid.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ViewerRootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition(ViewerRootGrid);
        _lastMousePosition = pos;

        // フルスクリーン時の上部メニューバー自動プルダウン表示制御
        UpdateFullScreenMenuPopup(pos);

        if (_isLoupeActive)
        {
            UpdateLoupePosition(pos);
        }

        if (_isLeftMouseDown && !_chordActionTriggered)
        {
            Vector delta = pos - _leftMouseDownPos;
            bool isScrollable = ImageScrollViewer.ExtentWidth > ImageScrollViewer.ViewportWidth + 4 ||
                                ImageScrollViewer.ExtentHeight > ImageScrollViewer.ViewportHeight + 4;
            if (delta.Length > 24 && isScrollable)
            {
                _isLeftDragging = true;
                _longPressTimer.Stop();
                ImageScrollViewer.ScrollToHorizontalOffset(_scrollStartOffset.X - delta.X);
                ImageScrollViewer.ScrollToVerticalOffset(_scrollStartOffset.Y - delta.Y);
            }
        }

        if (_isRightMouseDown && !_chordActionTriggered)
        {
            bool changed = _gestureEngine.AddPoint(pos);
            if (_gestureEngine.HasMovedBeyondThreshold)
            {
                _isRightGestureDragging = true;
                _longPressTimer.Stop();
                GestureOverlayCanvas.Visibility = Visibility.Visible;
                GestureTrailPolyline.Points.Add(pos);

                if (changed)
                {
                    string pattern = _gestureEngine.CurrentGesturePattern;
                    var (_, desc) = MouseGestureEngine.ResolveGesture(pattern);
                    GestureStatusText.Text = $"ジェスチャ [{pattern}] : {desc}";
                }
            }
        }
    }

    private void ViewerRootGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _longPressTimer.Stop();

        if (e.ChangedButton == MouseButton.Left)
        {
            bool wasDragging = _isLeftDragging;
            _isLeftMouseDown = false;
            _isLeftDragging = false;
            if (!_isRightMouseDown) ViewerRootGrid.ReleaseMouseCapture();

            if (!wasDragging && !_chordActionTriggered && !_longPressTriggered)
            {
                NextPage();
            }
            if (!_isRightMouseDown) _chordActionTriggered = false;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            bool wasGesture = _isRightGestureDragging;
            string pattern = _gestureEngine.CurrentGesturePattern;

            _isRightMouseDown = false;
            _isRightGestureDragging = false;
            GestureOverlayCanvas.Visibility = Visibility.Collapsed;
            if (!_isLeftMouseDown) ViewerRootGrid.ReleaseMouseCapture();

            if (wasGesture && !_chordActionTriggered)
            {
                ExecuteGestureAction(pattern);
            }
            else if (!wasGesture && !_chordActionTriggered && !_longPressTriggered)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || _imageFiles.Count == 0)
                {
                    ViewerContextMenu.PlacementTarget = ViewerRootGrid;
                    ViewerContextMenu.IsOpen = true;
                }
                else
                {
                    PrevPage();
                }
            }

            if (!_isLeftMouseDown) _chordActionTriggered = false;
            e.Handled = true;
        }
    }

    private void LongPressTimer_Tick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        _longPressTriggered = true;

        if (_longPressCandidateButton == MouseButton.Left && _isLeftMouseDown)
        {
            _isLeftMouseDown = false;
            Close();
        }
        else if (_longPressCandidateButton == MouseButton.Right && _isRightMouseDown && !_isRightGestureDragging)
        {
            TogglePageMode();
        }
    }

    private void ExecuteGestureAction(string pattern)
    {
        var (action, desc) = MouseGestureEngine.ResolveGesture(pattern);
        switch (action)
        {
            case GestureAction.FullScreenToggle:
                ToggleFullScreen();
                break;
            case GestureAction.AlignHeightTwoPage:
                ToggleAlignHeightTwoPageAndFixedScale();
                break;
            case GestureAction.FitEither:
                SetScaleMode(ScaleMode.FitEither, "縦横どちらか合わせる");
                break;
            case GestureAction.ThumbnailCustom:
                OpenThumbnailView(0, _settings.CustomThumbnailSize);
                break;
            case GestureAction.JumpForwardFixed:
                StepPage(+_settings.FixedJumpDistance);
                ShowOsd($"{_settings.FixedJumpDistance}ページ進む");
                break;
            case GestureAction.JumpBackwardFixed:
                StepPage(-_settings.FixedJumpDistance);
                ShowOsd($"{_settings.FixedJumpDistance}ページ戻る");
                break;
            case GestureAction.SpecifiedScale:
                SetScaleMode(ScaleMode.SpecifiedScale, $"指定スケール ({(int)(_settings.SpecifiedScaleFactor * 100)}%)");
                break;
            case GestureAction.QuickSave1:
                TriggerQuickSave(1);
                break;
            case GestureAction.QuickSave2:
                TriggerQuickSave(2);
                break;
            case GestureAction.QuickSave3:
                TriggerQuickSave(3);
                break;
            case GestureAction.QuickSave4:
                TriggerQuickSave(4);
                break;
            default:
                ShowOsd($"ジェスチャ [{pattern}] ({desc})");
                break;
        }
    }

    private void ViewerRootGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ThumbnailOverlayGrid.Visibility == Visibility.Visible) return;

        if (_isLeftMouseDown && e.Delta > 0)
        {
            _chordActionTriggered = true;
            _longPressTimer.Stop();
            TogglePageMode();
            e.Handled = true;
            return;
        }

        if (_isRightMouseDown)
        {
            _chordActionTriggered = true;
            _longPressTimer.Stop();
            if (_isLoupeActive)
            {
                _settings.LoupeScale = Math.Clamp(_settings.LoupeScale + (e.Delta > 0 ? 0.25 : -0.25), 1.25, 8.0);
                UpdateLoupePosition(_lastMousePosition);
                ShowOsd($"ルーペ倍率: {(int)(_settings.LoupeScale * 100)}%");
            }
            else
            {
                AdjustZoom(e.Delta > 0 ? 0.15 : -0.15);
            }
            e.Handled = true;
            return;
        }

        if (ImageScrollViewer.ScrollableHeight > 4)
        {
            double step = Math.Max(48, ViewerRootGrid.ActualHeight * 0.25);
            if (e.Delta > 0)
            {
                if (ImageScrollViewer.VerticalOffset > 0)
                    ImageScrollViewer.ScrollToVerticalOffset(Math.Max(0, ImageScrollViewer.VerticalOffset - step));
                else
                    PrevPage();
            }
            else
            {
                if (ImageScrollViewer.VerticalOffset < ImageScrollViewer.ScrollableHeight)
                    ImageScrollViewer.ScrollToVerticalOffset(Math.Min(ImageScrollViewer.ScrollableHeight, ImageScrollViewer.VerticalOffset + step));
                else
                    NextPage();
            }
        }
        else
        {
            if (e.Delta > 0) PrevPage();
            else NextPage();
        }

        e.Handled = true;
    }

    #endregion

    #region キーボードショートカット処理 (MangaMeeya.ini 準拠)

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys mods = Keyboard.Modifiers;

        if (ThumbnailOverlayGrid.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape)
            {
                CloseThumbnailView();
                e.Handled = true;
            }
            return;
        }

        if (mods == ModifierKeys.Alt && key == Key.Return)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (mods == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.O:
                    ShowOpenFileDialog();
                    e.Handled = true;
                    return;
                case Key.C:
                    CopyCurrentImageToClipboard();
                    e.Handled = true;
                    return;
                case Key.I:
                    ShowImageInfoDialog();
                    e.Handled = true;
                    return;
                case Key.Right:
                    if (_isLoupeActive)
                    {
                        _settings.LoupeRadius = Math.Clamp(_settings.LoupeRadius + 16, 60, 360);
                        ApplyLoupeSettings();
                        UpdateLoupePosition(_lastMousePosition);
                        ShowOsd($"ルーペ半径: {_settings.LoupeRadius}px");
                    }
                    else
                    {
                        MoveToSiblingFolder(+1);
                    }
                    e.Handled = true;
                    return;
                case Key.Left:
                    if (_isLoupeActive)
                    {
                        _settings.LoupeRadius = Math.Clamp(_settings.LoupeRadius - 16, 60, 360);
                        ApplyLoupeSettings();
                        UpdateLoupePosition(_lastMousePosition);
                        ShowOsd($"ルーペ半径: {_settings.LoupeRadius}px");
                    }
                    else
                    {
                        MoveToSiblingFolder(-1);
                    }
                    e.Handled = true;
                    return;
                case Key.Up:
                    if (_isLoupeActive)
                    {
                        _settings.LoupeScale = Math.Clamp(_settings.LoupeScale + 0.25, 1.25, 8.0);
                        UpdateLoupePosition(_lastMousePosition);
                        ShowOsd($"ルーペ倍率: {(int)(_settings.LoupeScale * 100)}%");
                        e.Handled = true;
                        return;
                    }
                    break;
                case Key.Down:
                    if (_isLoupeActive)
                    {
                        _settings.LoupeScale = Math.Clamp(_settings.LoupeScale - 0.25, 1.25, 8.0);
                        UpdateLoupePosition(_lastMousePosition);
                        ShowOsd($"ルーペ倍率: {(int)(_settings.LoupeScale * 100)}%");
                        e.Handled = true;
                        return;
                    }
                    break;
            }
        }

        if (mods == ModifierKeys.Shift)
        {
            switch (key)
            {
                case Key.Down:
                    StepPage(+1);
                    ShowOsd("1ページ進む");
                    e.Handled = true;
                    return;
                case Key.Up:
                    StepPage(-1);
                    ShowOsd("1ページ戻る");
                    e.Handled = true;
                    return;
                case Key.Right:
                    StepPage(+Math.Max(1, _imageFiles.Count / 10));
                    e.Handled = true;
                    return;
                case Key.Left:
                    StepPage(-Math.Max(1, _imageFiles.Count / 10));
                    e.Handled = true;
                    return;
            }
        }

        if (mods == ModifierKeys.Alt)
        {
            switch (key)
            {
                case Key.Right:
                    StepPage(+_settings.FixedJumpDistance);
                    e.Handled = true;
                    return;
                case Key.Left:
                    StepPage(-_settings.FixedJumpDistance);
                    e.Handled = true;
                    return;
            }
        }

        if (mods == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Right:
                case Key.PageDown:
                    NextPage();
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.PageUp:
                    PrevPage();
                    e.Handled = true;
                    break;
                case Key.Home:
                    GoToFirstPage();
                    e.Handled = true;
                    break;
                case Key.End:
                    GoToLastPage();
                    e.Handled = true;
                    break;
                case Key.F1:
                    OpenHelpManual();
                    e.Handled = true;
                    break;
                case Key.Space:
                    TogglePageMode();
                    e.Handled = true;
                    break;
                case Key.OemPipe:
                case Key.OemBackslash:
                    _settings.SpreadDirection = _settings.SpreadDirection == SpreadDirection.RightToLeft
                        ? SpreadDirection.LeftToRight
                        : SpreadDirection.RightToLeft;
                    ShowOsd(_settings.SpreadDirection == SpreadDirection.RightToLeft ? "右開き" : "左開き");
                    RenderCurrentPage();
                    e.Handled = true;
                    break;
                case Key.Z:
                    SetScaleMode(ScaleMode.FitEither, "縦横どちらか合わせる");
                    e.Handled = true;
                    break;
                case Key.X:
                    SetScaleMode(ScaleMode.FitWindow, "ウインドウサイズに合わせる");
                    e.Handled = true;
                    break;
                case Key.C:
                    SetScaleMode(ScaleMode.OriginalSize, "原寸サイズで表示");
                    e.Handled = true;
                    break;
                case Key.V:
                    SetScaleMode(ScaleMode.FixedScale, "倍率固定表示");
                    e.Handled = true;
                    break;
                case Key.B:
                    SetScaleMode(ScaleMode.SpecifiedScale, $"指定スケール ({(int)(_settings.SpecifiedScaleFactor * 100)}%)");
                    e.Handled = true;
                    break;
                case Key.N:
                    SetScaleMode(ScaleMode.FitWindowToImage, "画像サイズに合わせる");
                    e.Handled = true;
                    break;
                case Key.Add:
                case Key.OemPlus:
                    AdjustZoom(+0.15);
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    AdjustZoom(-0.15);
                    e.Handled = true;
                    break;
                case Key.L:
                    ToggleLoupe();
                    e.Handled = true;
                    break;
                case Key.A:
                    OpenThumbnailView(4, 240);
                    e.Handled = true;
                    break;
                case Key.H:
                    OpenThumbnailView(8, 150);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isLoupeActive) ToggleLoupe();
                    else if (_isFullScreen) ToggleFullScreen();
                    e.Handled = true;
                    break;
            }
        }
    }

    #endregion

    #region メニューバー＆コンテキストメニューハンドラ＆チェック状態同期

    private void UpdateMenuCheckStates()
    {
        if (MainMenuBar?.Items != null)
        {
            UpdateMenuItemsRecursive(MainMenuBar.Items);
        }
        if (ViewerContextMenu?.Items != null)
        {
            UpdateMenuItemsRecursive(ViewerContextMenu.Items);
        }
    }

    private void UpdateMenuItemsRecursive(ItemCollection items)
    {
        PageDisplayMode effectiveMode = _activeConnectedOverride?.SuggestedMode ?? _settings.PageMode;
        bool isSingleCurrentlyDisplayed = _imageFiles.Count > 0
            ? (_lastDisplayedPageCount == 1 && effectiveMode is PageDisplayMode.SinglePage or PageDisplayMode.TwoPage or PageDisplayMode.AutoSpread)
            : (_settings.PageMode == PageDisplayMode.SinglePage);
        bool isTwoHorizCurrentlyDisplayed = _imageFiles.Count > 0
            ? (_lastDisplayedPageCount == 2 && _currentGridRows == 1 && _currentGridCols == 2)
            : (_settings.PageMode is PageDisplayMode.TwoPage or PageDisplayMode.AutoSpread);

        foreach (object? obj in items)
        {
            if (obj is not MenuItem mi) continue;

            int id = ParseMenuId(mi.Uid);
            if (id != 0)
            {
                bool? checkState = id switch
                {
                    40009 => isSingleCurrentlyDisplayed,
                    40010 => isTwoHorizCurrentlyDisplayed,
                    40200 => _settings.PageMode is PageDisplayMode.TwoPage or PageDisplayMode.AutoSpread && !isSingleCurrentlyDisplayed,
                    40201 => effectiveMode == PageDisplayMode.Vertical2,
                    40202 => effectiveMode == PageDisplayMode.Horizontal3,
                    40203 => effectiveMode == PageDisplayMode.Vertical3,
                    40204 => effectiveMode == PageDisplayMode.Horizontal4,
                    40205 => effectiveMode == PageDisplayMode.Vertical4,
                    40206 => effectiveMode == PageDisplayMode.Grid2x2,
                    40210 => _settings.AutoDetectConnectedImages,
                    278 => _settings.SpreadDirection == SpreadDirection.LeftToRight,
                    279 => _settings.SpreadDirection == SpreadDirection.RightToLeft,
                    131 => _settings.AutoSpreadEnabled,
                    119 => _isFullScreen,
                    380 => _fullScreenMenuMode == 0,
                    381 => _fullScreenMenuMode == 1,
                    382 => _fullScreenMenuMode == 2,
                    172 => _isLoupeActive,
                    40100 => _settings.ScaleMode == ScaleMode.FixedScale,
                    40101 => _settings.ScaleMode == ScaleMode.FitEither,
                    40102 => _settings.ScaleMode == ScaleMode.FitWindow,
                    40103 => _settings.ScaleMode == ScaleMode.OriginalSize,
                    40104 => _settings.ScaleMode == ScaleMode.FitWindowToImage,
                    40105 => _settings.ScaleMode == ScaleMode.SpecifiedScale,
                    40106 => _settings.ScaleMode == ScaleMode.AlignHeightTwoPage,
                    40107 => _settings.ShrinkOnly,
                    >= 351 and <= 357 => (int)_settings.Filter == id,
                    40019 => _settings.PreloadEnabled,
                    >= 400 and <= 412 => (int)_currentSortMode == id,
                    >= 344 and <= 349 => (int)_settings.FolderSort == id,
                    _ => null
                };

                if (checkState.HasValue)
                {
                    mi.IsChecked = checkState.Value;
                }
            }

            if (mi.Items.Count > 0)
            {
                UpdateMenuItemsRecursive(mi.Items);
            }
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        e.Handled = true;

        int menuId = ParseMenuId(menuItem.Uid);
        switch (menuId)
        {
            case 40003: // 開く
                ShowOpenFileDialog();
                return;
            case 271: // フォルダを開く
                ShowOpenFolderDialog();
                return;
            case 40155:
                TriggerQuickSave(1);
                return;
            case 40156:
                TriggerQuickSave(2);
                return;
            case 40157:
                TriggerQuickSave(3);
                return;
            case 40158:
                TriggerQuickSave(4);
                return;
            case 40162: // クイックセーブ設定
                OpenQuickSaveSettingsDialog();
                return;
            case 280: // 閉じる
                _imageFiles.Clear();
                _cacheEngine.Clear();
                RenderCurrentPage();
                Title = "MangaMeYeah";
                return;
            case 281: // 更新
                if (!string.IsNullOrEmpty(_currentFolderPath)) OpenPath(_currentFolderPath);
                return;
            case 40023: // クリップボードへコピー
                CopyCurrentImageToClipboard();
                return;
            case 40009: // 1ページ表示
                SetPageDisplayMode(PageDisplayMode.SinglePage, "1ページ表示", forceTwoPageAtCurrent: false);
                return;
            case 40010: // 2ページ表示 (幅広画像でも強制的に2ページ表示可能)
            case 40200: // 横2枚表示 (標準見開き)
                SetPageDisplayMode(PageDisplayMode.TwoPage, "横2枚表示 (見開き)", forceTwoPageAtCurrent: true);
                return;
            case 40201: // 縦2枚表示
                SetPageDisplayMode(PageDisplayMode.Vertical2, "縦2枚表示");
                return;
            case 40202: // 横3枚表示
                SetPageDisplayMode(PageDisplayMode.Horizontal3, "横3枚表示");
                return;
            case 40203: // 縦3枚表示
                SetPageDisplayMode(PageDisplayMode.Vertical3, "縦3枚表示");
                return;
            case 40204: // 横4枚表示
                SetPageDisplayMode(PageDisplayMode.Horizontal4, "横4枚表示");
                return;
            case 40205: // 縦4枚表示
                SetPageDisplayMode(PageDisplayMode.Vertical4, "縦4枚表示");
                return;
            case 40206: // 縦横2×2枚表示
                SetPageDisplayMode(PageDisplayMode.Grid2x2, "縦横2×2枚表示 (4枚)");
                return;
            case 40210: // 自動連結画像検出
                _settings.AutoDetectConnectedImages = !_settings.AutoDetectConnectedImages;
                _lastEvaluatedConnectedIndex = -1;
                ShowOsd(_settings.AutoDetectConnectedImages ? "自動連結画像検出: ON" : "自動連結画像検出: OFF");
                RenderCurrentPage();
                return;
            case 278: // 左開き
                _settings.SpreadDirection = SpreadDirection.LeftToRight;
                ShowOsd("見開き方向: 左開き");
                RenderCurrentPage();
                return;
            case 279: // 右開き
                _settings.SpreadDirection = SpreadDirection.RightToLeft;
                ShowOsd("見開き方向: 右開き");
                RenderCurrentPage();
                return;
            case 131: // 自動見開き表示
                _settings.AutoSpreadEnabled = !_settings.AutoSpreadEnabled;
                _forcedTwoPageIndex = -1;
                ShowOsd(_settings.AutoSpreadEnabled ? "自動見開き表示: ON" : "自動見開き表示: OFF");
                RenderCurrentPage();
                return;
            case 40011: // サムネイル表示(1画面)
                OpenThumbnailView(4, 240);
                return;
            case 40012: // サムネイル表示(8列)
                OpenThumbnailView(8, 150);
                return;
            case 40013:
            case 232:   // サムネイル表示(カスタム)
                OpenThumbnailView(0, _settings.CustomThumbnailSize);
                return;
            case 119: // フルスクリーン
                ToggleFullScreen();
                return;
            case 380: // フルスクリーン時: メニュー固定表示
                _fullScreenMenuMode = 0;
                if (_isFullScreen) MainMenuBar.Visibility = Visibility.Visible;
                UpdateMenuCheckStates();
                ShowOsd("フルスクリーン時メニュー: 固定表示");
                return;
            case 381: // フルスクリーン時: メニューポップアップ表示
                _fullScreenMenuMode = 1;
                if (_isFullScreen) MainMenuBar.Visibility = Visibility.Collapsed;
                UpdateMenuCheckStates();
                ShowOsd("フルスクリーン時メニュー: ポップアップ表示");
                return;
            case 382: // フルスクリーン時: メニュー非表示
                _fullScreenMenuMode = 2;
                if (_isFullScreen) MainMenuBar.Visibility = Visibility.Collapsed;
                UpdateMenuCheckStates();
                ShowOsd("フルスクリーン時メニュー: 非表示");
                return;
            case 172: // ルーペ表示
                ToggleLoupe();
                UpdateMenuCheckStates();
                return;
            case 40100:
                SetScaleMode(ScaleMode.FixedScale, $"倍率固定(デフォルト) ({(int)Math.Round(_settings.FixedZoomFactor * 100)}%)");
                return;
            case 40101:
                SetScaleMode(ScaleMode.FitEither, "縦横どちらか合わせる");
                return;
            case 40102:
                SetScaleMode(ScaleMode.FitWindow, "ウインドウサイズに合わせる");
                return;
            case 40103:
                SetScaleMode(ScaleMode.OriginalSize, "原寸サイズで表示");
                return;
            case 40104:
                SetScaleMode(ScaleMode.FitWindowToImage, "画像サイズに合わせる");
                return;
            case 40105:
                SetScaleMode(ScaleMode.SpecifiedScale, $"指定スケール ({(int)(_settings.SpecifiedScaleFactor * 100)}%)");
                return;
            case 40106:
                SetScaleMode(ScaleMode.AlignHeightTwoPage, "縦サイズを揃える(2ページ表示用)");
                return;
            case 40107:
                _settings.ShrinkOnly = !_settings.ShrinkOnly;
                ShowOsd(_settings.ShrinkOnly ? "縮小はするが拡大はしない: ON" : "縮小はするが拡大はしない: OFF");
                RenderCurrentPage();
                return;
            case 351:
            case 352:
            case 353:
            case 354:
            case 355:
            case 356:
            case 357:
                _settings.Filter = (InterpolationFilter)menuId;
                _cacheEngine.Clear();
                ShowOsd($"拡縮フィルタ変更: {menuItem.Tag}");
                RenderCurrentPage();
                return;
            case 143:
                NextPage();
                return;
            case 144:
                StepPage(+1);
                return;
            case 145:
                StepPage(+10);
                return;
            case 146:
                StepPage(+20);
                return;
            case 147:
                StepPage(+50);
                return;
            case 148:
                if (_imageFiles.Count > 0) { _forcedTwoPageIndex = -1; _currentIndex = _imageFiles.Count - 1; RenderCurrentPage(); }
                return;
            case 149:
                PrevPage();
                return;
            case 150:
                StepPage(-1);
                return;
            case 151:
                StepPage(-10);
                return;
            case 152:
                StepPage(-20);
                return;
            case 153:
                StepPage(-50);
                return;
            case 154:
                if (_imageFiles.Count > 0) { _forcedTwoPageIndex = -1; _currentIndex = 0; RenderCurrentPage(); }
                return;
            case 188:
                MoveToSiblingFolder(+1);
                return;
            case 189:
                MoveToSiblingFolder(-1);
                return;
            case 167:
                AdjustZoom(+0.15);
                return;
            case 168:
                AdjustZoom(-0.15);
                return;
            case 40019:
                _settings.PreloadEnabled = !_settings.PreloadEnabled;
                UpdateMenuCheckStates();
                ShowOsd(_settings.PreloadEnabled ? "ファイル先読み: ON" : "ファイル先読み: OFF");
                return;
            case >= 400 and <= 412:
                _currentSortMode = (FileSortMode)menuId;
                UpdateMenuCheckStates();
                if (_imageFiles.Count > 0)
                {
                    string currentFile = _imageFiles[_currentIndex];
                    _imageFiles = ImageFolderService.SortFilesHierarchical(_imageFiles, _currentSortMode);
                    int idx = _imageFiles.IndexOf(currentFile);
                    _currentIndex = idx >= 0 ? idx : 0;
                    _lastEvaluatedConnectedIndex = -1;
                    RenderCurrentPage();
                }
                ShowOsd($"ファイルソート変更: {menuItem.Tag}");
                return;
            case >= 344 and <= 349:
                _settings.FolderSort = (FolderSortMode)menuId;
                UpdateMenuCheckStates();
                ShowOsd($"フォルダソート変更: {menuItem.Tag}");
                return;
            case 341: // レジストリ関連設定
                OpenRegistrySettingsDialog();
                return;
            case 124: // 環境設定
                OpenPreferencesDialog();
                return;
            case 114:
                ShowImageInfoDialog();
                return;
            case 40004:
                ShowCacheInfoDialog();
                return;
        }

        string featureName = menuItem.Tag as string ?? menuItem.Header?.ToString() ?? "選択された機能";
        MessageBox.Show(
            this,
            $"{featureName} は未実装です",
            "未実装",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static int ParseMenuId(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return 0;
        int idx = uid.IndexOf('_');
        string numPart = idx >= 0 ? uid[(idx + 1)..] : uid;
        return int.TryParse(numPart, out int id) ? id : 0;
    }

    private void ShowOpenFileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "画像・書庫ファイルを開く",
            Filter = "対応ファイル (*.jpg;*.png;*.zip;*.cbz;*.rar;*.7z...)|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.tif;*.tiff;*.avif;*.jxl;*.heic;*.cr2;*.cr3;*.nef;*.arw;*.orf;*.raf;*.rw2;*.dng;*.zip;*.cbz;*.rar;*.cbr;*.7z|画像ファイル|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.tif;*.tiff;*.avif;*.jxl;*.heic;*.cr2;*.cr3;*.nef;*.arw;*.orf;*.raf;*.rw2;*.dng|書庫ファイル (*.zip;*.cbz;*.rar;*.cbr;*.7z)|*.zip;*.cbz;*.rar;*.cbr;*.7z|すべてのファイル (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
        {
            dlg.InitialDirectory = _currentFolderPath;
        }
        else if (!string.IsNullOrEmpty(_currentFolderPath) && File.Exists(_currentFolderPath))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(_currentFolderPath);
        }
        else if (!string.IsNullOrEmpty(_settings.InitialFolderPath) && Directory.Exists(_settings.InitialFolderPath))
        {
            dlg.InitialDirectory = _settings.InitialFolderPath;
        }
        if (dlg.ShowDialog(this) == true)
        {
            OpenPath(dlg.FileName);
        }
    }

    private void ShowOpenFolderDialog()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "画像フォルダを開く"
        };
        if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
        {
            dlg.InitialDirectory = _currentFolderPath;
        }
        else if (!string.IsNullOrEmpty(_settings.InitialFolderPath) && Directory.Exists(_settings.InitialFolderPath))
        {
            dlg.InitialDirectory = _settings.InitialFolderPath;
        }
        if (dlg.ShowDialog(this) == true)
        {
            OpenPath(dlg.FolderName);
        }
    }

    private void CopyCurrentImageToClipboard()
    {
        if (_currentDisplayedEntries.Count > 0)
        {
            Clipboard.SetImage(_currentDisplayedEntries[0].Bitmap);
            ShowOsd("クリップボードへコピーしました");
        }
    }

    private ImageInfoWindow? _imageInfoWindow;

    private void EnsureColorCountsForCurrentSlots()
    {
        foreach (var slot in _currentDisplayedSlots)
        {
            if (slot.Entry.UniqueColorCount == 0)
            {
                var withColors = _cacheEngine.GetOrLoadPage(slot.Entry.FilePath, 0, 0, _settings, countColors: true);
                slot.Entry = withColors;
            }
        }
    }

    private void ShowImageInfoDialog()
    {
        if (_imageFiles.Count == 0 || _currentDisplayedSlots.Count == 0)
        {
            MessageBox.Show(this, "画像が開かれていません。", "画像情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EnsureColorCountsForCurrentSlots();

        if (_imageInfoWindow != null && _imageInfoWindow.IsLoaded)
        {
            _imageInfoWindow.Activate();
            _imageInfoWindow.UpdateSlots(_currentDisplayedSlots, _currentGridRows, _currentGridCols);
            return;
        }

        _imageInfoWindow = new ImageInfoWindow(_currentDisplayedSlots, _currentGridRows, _currentGridCols, this)
        {
            Owner = this
        };
        _imageInfoWindow.Closed += (_, _) => _imageInfoWindow = null;
        _imageInfoWindow.Show();
    }

    private void ShowCacheInfoDialog()
    {
        double usedMb = _cacheEngine.TotalEstimatedMemoryBytes / (1024.0 * 1024.0);
        string msg =
            $"キャッシュ保持ページ数: {_cacheEngine.CachedPageCount} ページ (上限 {_settings.MaxCachedBitmapCount} ページ)\n" +
            $"推定キャッシュ使用量: {usedMb:F1} MB / 上限 {_settings.CacheMemoryLimitMb:N0} MB\n" +
            $"先読み設定: {(_settings.PreloadEnabled ? $"有効 (進む方向 {_settings.PreloadForwardCount}枚 / 戻る方向 {_settings.PreloadBackwardCount}枚)" : "無効")}\n" +
            $"キャッシュヒット数: {_cacheEngine.CacheHits} 回 (ミス: {_cacheEngine.CacheMisses} 回)";

        MessageBox.Show(this, msg, "キャッシュ情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HelpManualMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenHelpManual();
        e.Handled = true;
    }

    private void OpenHelpManual()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string[] searchPaths =
            [
                Path.Combine(baseDir, "docs", "help", "index.md"),
                Path.Combine(baseDir, "..", "..", "..", "..", "docs", "help", "index.md"),
                Path.Combine(baseDir, "..", "..", "..", "docs", "help", "index.md"),
                Path.Combine(Directory.GetCurrentDirectory(), "docs", "help", "index.md")
            ];

            string? helpFile = searchPaths.FirstOrDefault(File.Exists);
            if (helpFile != null)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(helpFile),
                    UseShellExecute = true
                };
                Process.Start(psi);
                ShowOsd("マニュアルを開きました");
            }
            else
            {
                // Fallback to README
                string readmePath = Path.Combine(baseDir, "README.md");
                if (File.Exists(readmePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = readmePath, UseShellExecute = true });
                }
                else
                {
                    ShowOsd("マニュアルファイルが見つかりませんでした");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"マニュアルの表示に失敗しました: {ex.Message}", "ヘルプ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void VersionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var versionWindow = new VersionWindow { Owner = this };
        versionWindow.ShowDialog();
        e.Handled = true;
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    #endregion

    #region ドラッグ＆ドロップ・リサイズ・モニター移動・最大化対応・OSD 補助

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            CloseThumbnailView();
            OpenPath(files[0]);
        }
    }

    private void ViewerRootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_imageFiles.Count == 0) return;
        if (Math.Abs(e.NewSize.Width - _lastRenderedViewportW) < 2 &&
            Math.Abs(e.NewSize.Height - _lastRenderedViewportH) < 2)
        {
            return;
        }

        _lastRenderedViewportW = e.NewSize.Width;
        _lastRenderedViewportH = e.NewSize.Height;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_isRendering && _imageFiles.Count > 0)
            {
                RenderCurrentPage();
            }
        });
    }

    private void ShowOsd(string message)
    {
        if (!_settings.ShowOsdText) return;
        OsdStatusText.Text = message;
        OsdStatusBorder.Visibility = Visibility.Visible;
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}