using System.Windows;
using System.Windows.Controls;

namespace MangaMeYeah;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void UnimplementedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            string featureName = menuItem.Tag as string ?? menuItem.Header?.ToString() ?? "選択された機能";
            MessageBox.Show(
                this,
                $"{featureName} は未実装です",
                "未実装",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Handled = true;
        }
    }

    private void VersionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var versionWindow = new VersionWindow
        {
            Owner = this
        };
        versionWindow.ShowDialog();
        e.Handled = true;
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}