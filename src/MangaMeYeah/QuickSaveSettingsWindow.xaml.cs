using System.IO;
using System.Windows;
using System.Windows.Controls;
using MangaMeYeah.Models;
using Microsoft.Win32;

namespace MangaMeYeah;

public partial class QuickSaveSettingsWindow : Window
{
    private readonly ViewerSettings _settings;

    public QuickSaveSettingsWindow(ViewerSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        while (_settings.QuickSaveSlots.Count < 4)
        {
            _settings.QuickSaveSlots.Add(new QuickSaveSlot { SlotNumber = _settings.QuickSaveSlots.Count + 1 });
        }

        Folder1TextBox.Text = _settings.QuickSaveSlots[0].FolderPath;
        Prefix1CheckBox.IsChecked = _settings.QuickSaveSlots[0].PrependFolderName;

        Folder2TextBox.Text = _settings.QuickSaveSlots[1].FolderPath;
        Prefix2CheckBox.IsChecked = _settings.QuickSaveSlots[1].PrependFolderName;

        Folder3TextBox.Text = _settings.QuickSaveSlots[2].FolderPath;
        Prefix3CheckBox.IsChecked = _settings.QuickSaveSlots[2].PrependFolderName;

        Folder4TextBox.Text = _settings.QuickSaveSlots[3].FolderPath;
        Prefix4CheckBox.IsChecked = _settings.QuickSaveSlots[3].PrependFolderName;
    }

    private static void BrowseForFolder(TextBox targetTextBox)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "クイックセーブ保存先フォルダを選択"
        };
        if (!string.IsNullOrWhiteSpace(targetTextBox.Text) && Directory.Exists(targetTextBox.Text))
        {
            dialog.InitialDirectory = targetTextBox.Text;
        }
        if (dialog.ShowDialog() == true)
        {
            targetTextBox.Text = dialog.FolderName;
        }
    }

    private void Browse1_Click(object sender, RoutedEventArgs e) => BrowseForFolder(Folder1TextBox);
    private void Browse2_Click(object sender, RoutedEventArgs e) => BrowseForFolder(Folder2TextBox);
    private void Browse3_Click(object sender, RoutedEventArgs e) => BrowseForFolder(Folder3TextBox);
    private void Browse4_Click(object sender, RoutedEventArgs e) => BrowseForFolder(Folder4TextBox);

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.QuickSaveSlots[0].FolderPath = Folder1TextBox.Text.Trim();
        _settings.QuickSaveSlots[0].PrependFolderName = Prefix1CheckBox.IsChecked == true;

        _settings.QuickSaveSlots[1].FolderPath = Folder2TextBox.Text.Trim();
        _settings.QuickSaveSlots[1].PrependFolderName = Prefix2CheckBox.IsChecked == true;

        _settings.QuickSaveSlots[2].FolderPath = Folder3TextBox.Text.Trim();
        _settings.QuickSaveSlots[2].PrependFolderName = Prefix3CheckBox.IsChecked == true;

        _settings.QuickSaveSlots[3].FolderPath = Folder4TextBox.Text.Trim();
        _settings.QuickSaveSlots[3].PrependFolderName = Prefix4CheckBox.IsChecked == true;

        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
