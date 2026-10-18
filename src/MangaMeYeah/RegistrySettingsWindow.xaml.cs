using System.Windows;
using MangaMeYeah.Services;

namespace MangaMeYeah;

public partial class RegistrySettingsWindow : Window
{
    public string StatusMessage { get; private set; } = string.Empty;

    public RegistrySettingsWindow()
    {
        InitializeComponent();
        bool isReg = ExplorerContextMenuService.IsRegistered();
        FolderMenuCheckBox.IsChecked = true;
        FolderBgMenuCheckBox.IsChecked = true;
        FileMenuCheckBox.IsChecked = isReg;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var (success, msg) = ExplorerContextMenuService.Register(
            includeFolder: FolderMenuCheckBox.IsChecked == true,
            includeFolderBackground: FolderBgMenuCheckBox.IsChecked == true,
            includeFiles: FileMenuCheckBox.IsChecked == true);

        StatusMessage = msg;
        if (!success)
        {
            MessageBox.Show(this, msg, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var (success, msg) = ExplorerContextMenuService.UnregisterAll();
        StatusMessage = msg;
        if (success)
        {
            FolderMenuCheckBox.IsChecked = false;
            FolderBgMenuCheckBox.IsChecked = false;
            FileMenuCheckBox.IsChecked = false;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(this, msg, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
