using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MangaMeYeah.Models;
using MangaMeYeah.Services;
using Microsoft.Win32;

namespace MangaMeYeah;

public partial class PreferencesWindow : Window
{
    private readonly ViewerSettings _settings;
    private readonly Action _onApplied;
    private bool _isInitializing = true;
    private bool _syncingLoupeControls;

    public PreferencesWindow(ViewerSettings settings, Action onApplied)
    {
        InitializeComponent();
        _settings = settings;
        _onApplied = onApplied;

        LoadSettingsIntoControls();
        _isInitializing = false;
        ApplyButton.IsEnabled = false;
    }

    private void LoadSettingsIntoControls()
    {
        // Tab 1: 操作設定
        ZoomStepTextBox.Text = _settings.ZoomStepPercent.ToString();
        CurrentScaleTextBox.Text = ((int)Math.Round(_settings.FixedZoomFactor * 100)).ToString();
        ResetScaleOnSameModeCheck.IsChecked = _settings.ResetScaleOnSameMode;

        StepNextPrevTextBox.Text = _settings.StepNextPrevCount.ToString();
        PageUpDownCountTextBox.Text = _settings.PageUpDownCount.ToString();
        FixedJumpTextBox.Text = _settings.FixedJumpDistance.ToString();
        FixedRatioJumpTextBox.Text = _settings.FixedRatioJumpCount.ToString();
        CheckImageSizeOnMoveCheck.IsChecked = _settings.CheckImageSizeOnMove;

        DragScroll1TextBox.Text = _settings.DragScrollPixel.ToString();
        KeyScrollXTextBox.Text = _settings.KeyScrollStepX.ToString();
        KeyScrollYTextBox.Text = _settings.KeyScrollStepY.ToString();
        WheelScrollXTextBox.Text = _settings.WheelScrollStepX.ToString();
        WheelScrollYTextBox.Text = _settings.WheelScrollStepY.ToString();

        ScrollUnitWindowRadio.IsChecked = _settings.ScrollUnitMode == 0;
        ScrollUnitImageRadio.IsChecked = _settings.ScrollUnitMode == 1;
        ScrollUnitPixelRadio.IsChecked = _settings.ScrollUnitMode == 2;

        // Tab 2: 一般設定 1
        SubfolderConfirmRadio.IsChecked = _settings.SubfolderReadMode == 0;
        SubfolderIncludeRadio.IsChecked = _settings.SubfolderReadMode == 1;
        SubfolderExcludeRadio.IsChecked = _settings.SubfolderReadMode == 2;
        IncludeFolderArchivesCheck.IsChecked = _settings.IncludeFolderArchivesAsSubfolders;
        IncludeArchiveArchivesCheck.IsChecked = _settings.IncludeArchiveArchivesAsSubfolders;
        InitialFolderTextBox.Text = _settings.InitialFolderPath;
        OpenFromStartRadio.IsChecked = _settings.StartupOpenMode != 3;
        OpenThumbnailRadio.IsChecked = _settings.StartupOpenMode == 3;
        AddBlankFirstPageCheck.IsChecked = _settings.AddBlankFirstPage;
        AutoDetectConnectedCheck.IsChecked = _settings.AutoDetectConnectedImages;
        SuppressConnectedPromptCheck.IsChecked = _settings.SuppressConnectedImagePrompt;

        // Tab 3: 一般設定 2
        LoupeCircleRadio.IsChecked = _settings.LoupeIsCircle;
        LoupeSquareRadio.IsChecked = !_settings.LoupeIsCircle;
        LoupeBorderCheck.IsChecked = _settings.LoupeShowBorder;
        LoupeClampCheck.IsChecked = _settings.LoupeClampToScreen;
        SuppressPopupsDuringLoupeCheck.IsChecked = _settings.SuppressPopupsDuringLoupe;

        _syncingLoupeControls = true;
        int radiusPx = (int)Math.Clamp(_settings.LoupeRadius, 40, 640);
        LoupeRadiusSlider.Value = radiusPx;
        LoupeRadiusTextBox.Text = radiusPx.ToString();

        int scalePct = (int)Math.Clamp(_settings.LoupeScale * 100.0, 100, 800);
        LoupeScaleSlider.Value = scalePct;
        LoupeScaleTextBox.Text = scalePct.ToString();
        _syncingLoupeControls = false;

        BgColorHexTextBox.Text = string.IsNullOrWhiteSpace(_settings.BackgroundColorHex) ? "#808080" : _settings.BackgroundColorHex;
        UpdateBgColorPreview(BgColorHexTextBox.Text);

        BookshelfBgColorHexTextBox.Text = string.IsNullOrWhiteSpace(_settings.BookshelfBackgroundColorHex) ? "#808080" : _settings.BookshelfBackgroundColorHex;
        UpdateBookshelfBgColorPreview(BookshelfBgColorHexTextBox.Text);

        // Tab 4: その他設定
        ShowScrollBarsCheck.IsChecked = _settings.ShowScrollBars;
        ShowSubfolderCaptionCheck.IsChecked = _settings.ShowSubfolderInCaption;
        ShowOsdTextCheck.IsChecked = _settings.ShowOsdText;
        FullScreenHideCursorCheck.IsChecked = _settings.FullScreenHideCursor;
        FullScreenTopMostCheck.IsChecked = _settings.FullScreenTopMost;
        OpenLastFileCheck.IsChecked = _settings.OpenLastFileOnStartup;
        SaveWindowPlacementCheck.IsChecked = _settings.SaveWindowPlacementOnExit;
        PreventMultipleInstancesCheck.IsChecked = _settings.PreventMultipleInstances;

        // Tab 5: キャッシュ設定
        MaxCachedBitmapTextBox.Text = _settings.MaxCachedBitmapCount.ToString();
        ResizeCacheCheck.IsChecked = _settings.ResizeCacheEnabled;
        PreloadForwardTextBox.Text = _settings.PreloadForwardCount.ToString();
        PreloadBackwardTextBox.Text = _settings.PreloadBackwardCount.ToString();
        CacheMemoryLimitTextBox.Text = _settings.CacheMemoryLimitMb.ToString();
    }

    private void SaveControlsToSettings()
    {
        // Tab 1: 操作設定
        if (int.TryParse(ZoomStepTextBox.Text, out int zoomStep))
            _settings.ZoomStepPercent = Math.Clamp(zoomStep, 1, 200);
        if (int.TryParse(CurrentScaleTextBox.Text, out int curScale))
            _settings.FixedZoomFactor = Math.Clamp(curScale / 100.0, 0.05, 16.0);
        _settings.ResetScaleOnSameMode = ResetScaleOnSameModeCheck.IsChecked == true;

        if (int.TryParse(StepNextPrevTextBox.Text, out int stepNP))
            _settings.StepNextPrevCount = Math.Clamp(stepNP, 1, 100);
        if (int.TryParse(PageUpDownCountTextBox.Text, out int pud))
            _settings.PageUpDownCount = Math.Clamp(pud, 1, 500);
        if (int.TryParse(FixedJumpTextBox.Text, out int fixedJump))
            _settings.FixedJumpDistance = Math.Clamp(fixedJump, 1, 1000);
        if (int.TryParse(FixedRatioJumpTextBox.Text, out int ratioJump))
            _settings.FixedRatioJumpCount = Math.Clamp(ratioJump, 1, 100);
        _settings.CheckImageSizeOnMove = CheckImageSizeOnMoveCheck.IsChecked == true;

        if (int.TryParse(DragScroll1TextBox.Text, out int drag1))
            _settings.DragScrollPixel = Math.Clamp(drag1, 1, 50);
        if (int.TryParse(KeyScrollXTextBox.Text, out int ksx))
            _settings.KeyScrollStepX = Math.Clamp(ksx, 1, 2000);
        if (int.TryParse(KeyScrollYTextBox.Text, out int ksy))
            _settings.KeyScrollStepY = Math.Clamp(ksy, 1, 2000);
        if (int.TryParse(WheelScrollXTextBox.Text, out int wsx))
            _settings.WheelScrollStepX = Math.Clamp(wsx, 1, 2000);
        if (int.TryParse(WheelScrollYTextBox.Text, out int wsy))
            _settings.WheelScrollStepY = Math.Clamp(wsy, 1, 2000);

        if (ScrollUnitWindowRadio.IsChecked == true) _settings.ScrollUnitMode = 0;
        else if (ScrollUnitImageRadio.IsChecked == true) _settings.ScrollUnitMode = 1;
        else _settings.ScrollUnitMode = 2;

        // Tab 2: 一般設定 1
        if (SubfolderConfirmRadio.IsChecked == true) _settings.SubfolderReadMode = 0;
        else if (SubfolderExcludeRadio.IsChecked == true) _settings.SubfolderReadMode = 2;
        else _settings.SubfolderReadMode = 1;
        _settings.IncludeFolderArchivesAsSubfolders = IncludeFolderArchivesCheck.IsChecked == true;
        _settings.IncludeArchiveArchivesAsSubfolders = IncludeArchiveArchivesCheck.IsChecked == true;
        _settings.InitialFolderPath = InitialFolderTextBox.Text.Trim();
        _settings.StartupOpenMode = OpenThumbnailRadio.IsChecked == true ? 3 : 1;
        _settings.AddBlankFirstPage = AddBlankFirstPageCheck.IsChecked == true;
        _settings.AutoDetectConnectedImages = AutoDetectConnectedCheck.IsChecked == true;
        _settings.SuppressConnectedImagePrompt = SuppressConnectedPromptCheck.IsChecked == true;

        // Tab 3: 一般設定 2
        _settings.LoupeIsCircle = LoupeCircleRadio.IsChecked == true;
        _settings.LoupeShowBorder = LoupeBorderCheck.IsChecked == true;
        _settings.LoupeClampToScreen = LoupeClampCheck.IsChecked == true;
        _settings.SuppressPopupsDuringLoupe = SuppressPopupsDuringLoupeCheck.IsChecked == true;

        if (int.TryParse(LoupeRadiusTextBox.Text, out int rad))
            _settings.LoupeRadius = Math.Clamp(rad, 40, 640);
        if (int.TryParse(LoupeScaleTextBox.Text, out int lscale))
            _settings.LoupeScale = Math.Clamp(lscale / 100.0, 1.0, 8.0);

        string bgHex = BgColorHexTextBox.Text.Trim();
        if (TryParseBrush(bgHex, out _))
        {
            _settings.BackgroundColorHex = bgHex;
        }

        string bShelfHex = BookshelfBgColorHexTextBox.Text.Trim();
        if (TryParseBrush(bShelfHex, out _))
        {
            _settings.BookshelfBackgroundColorHex = bShelfHex;
        }

        // Tab 4: その他設定
        _settings.ShowScrollBars = ShowScrollBarsCheck.IsChecked == true;
        _settings.ShowSubfolderInCaption = ShowSubfolderCaptionCheck.IsChecked == true;
        _settings.ShowOsdText = ShowOsdTextCheck.IsChecked == true;
        _settings.FullScreenHideCursor = FullScreenHideCursorCheck.IsChecked == true;
        _settings.FullScreenTopMost = FullScreenTopMostCheck.IsChecked == true;
        _settings.OpenLastFileOnStartup = OpenLastFileCheck.IsChecked == true;
        _settings.SaveWindowPlacementOnExit = SaveWindowPlacementCheck.IsChecked == true;
        _settings.PreventMultipleInstances = PreventMultipleInstancesCheck.IsChecked == true;

        // Tab 5: キャッシュ設定
        if (int.TryParse(MaxCachedBitmapTextBox.Text, out int maxBmp))
            _settings.MaxCachedBitmapCount = Math.Clamp(maxBmp, 4, 512);
        _settings.ResizeCacheEnabled = ResizeCacheCheck.IsChecked == true;
        if (int.TryParse(PreloadForwardTextBox.Text, out int pf))
            _settings.PreloadForwardCount = Math.Clamp(pf, 0, 100);
        if (int.TryParse(PreloadBackwardTextBox.Text, out int pb))
            _settings.PreloadBackwardCount = Math.Clamp(pb, 0, 100);
        if (int.TryParse(CacheMemoryLimitTextBox.Text, out int memMb))
            _settings.CacheMemoryLimitMb = Math.Clamp(memMb, 0, 65536);

        _settings.Save();
        _onApplied();
        ApplyButton.IsEnabled = false;
    }

    private void SettingControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        ApplyButton.IsEnabled = true;
    }

    private void LoupeRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingLoupeControls || LoupeRadiusTextBox == null) return;
        _syncingLoupeControls = true;
        LoupeRadiusTextBox.Text = ((int)Math.Round(e.NewValue)).ToString();
        _syncingLoupeControls = false;
        if (!_isInitializing) ApplyButton.IsEnabled = true;
    }

    private void LoupeRadiusTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingLoupeControls || LoupeRadiusSlider == null) return;
        if (int.TryParse(LoupeRadiusTextBox.Text, out int val))
        {
            _syncingLoupeControls = true;
            LoupeRadiusSlider.Value = Math.Clamp(val, 40, 640);
            _syncingLoupeControls = false;
        }
        if (!_isInitializing) ApplyButton.IsEnabled = true;
    }

    private void LoupeScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingLoupeControls || LoupeScaleTextBox == null) return;
        _syncingLoupeControls = true;
        LoupeScaleTextBox.Text = ((int)Math.Round(e.NewValue)).ToString();
        _syncingLoupeControls = false;
        if (!_isInitializing) ApplyButton.IsEnabled = true;
    }

    private void LoupeScaleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingLoupeControls || LoupeScaleSlider == null) return;
        if (int.TryParse(LoupeScaleTextBox.Text, out int val))
        {
            _syncingLoupeControls = true;
            LoupeScaleSlider.Value = Math.Clamp(val, 100, 800);
            _syncingLoupeControls = false;
        }
        if (!_isInitializing) ApplyButton.IsEnabled = true;
    }

    private void BrowseInitialFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "初期フォルダを選択"
        };
        if (Directory.Exists(InitialFolderTextBox.Text))
        {
            dlg.InitialDirectory = InitialFolderTextBox.Text;
        }
        if (dlg.ShowDialog(this) == true)
        {
            InitialFolderTextBox.Text = dlg.FolderName;
            ApplyButton.IsEnabled = true;
        }
    }

    private void BgColorHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BgColorPreviewBorder == null) return;
        UpdateBgColorPreview(BgColorHexTextBox.Text.Trim());
        if (!_isInitializing) ApplyButton.IsEnabled = true;
    }

    private void SetGrayBg_Click(object sender, RoutedEventArgs e)
    {
        BgColorHexTextBox.Text = "#808080";
    }

    private void SetDarkBg_Click(object sender, RoutedEventArgs e)
    {
        BgColorHexTextBox.Text = "#141414";
    }

    private void ChooseBgColor_Click(object sender, RoutedEventArgs e)
    {
        Color initial = Colors.Gray;
        if (TryParseColor(BgColorHexTextBox.Text.Trim(), out var parsed)) initial = parsed;

        var selected = NativeColorDialogService.PromptColor(this, initial);
        if (selected.HasValue)
        {
            string hex = $"#{selected.Value.R:X2}{selected.Value.G:X2}{selected.Value.B:X2}";
            BgColorHexTextBox.Text = hex;
        }
    }

    private void ChooseBookshelfBgColor_Click(object sender, RoutedEventArgs e)
    {
        Color initial = Colors.Gray;
        if (TryParseColor(BookshelfBgColorHexTextBox.Text.Trim(), out var parsed)) initial = parsed;

        var selected = NativeColorDialogService.PromptColor(this, initial);
        if (selected.HasValue)
        {
            string hex = $"#{selected.Value.R:X2}{selected.Value.G:X2}{selected.Value.B:X2}";
            BookshelfBgColorHexTextBox.Text = hex;
        }
    }

    private void BookshelfBgColorHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BookshelfBgColorPreviewBorder == null) return;
        UpdateBookshelfBgColorPreview(BookshelfBgColorHexTextBox.Text.Trim());
        if (!_isInitializing) ApplyButton.IsEnabled = true;
    }

    private void UpdateBookshelfBgColorPreview(string hex)
    {
        if (TryParseBrush(hex, out var brush) && brush != null)
        {
            BookshelfBgColorPreviewBorder.Background = brush;
        }
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        color = Colors.Gray;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                color = c;
                return true;
            }
        }
        catch { }
        return false;
    }

    private void UpdateBgColorPreview(string hex)
    {
        if (TryParseBrush(hex, out var brush) && brush != null)
        {
            BgColorPreviewBorder.Background = brush;
        }
    }

    private static bool TryParseBrush(string hex, out SolidColorBrush? brush)
    {
        brush = null;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                brush = new SolidColorBrush(c);
                brush.Freeze();
                return true;
            }
        }
        catch
        {
            // Ignore invalid color string
        }
        return false;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SaveControlsToSettings();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        SaveControlsToSettings();
    }
}
