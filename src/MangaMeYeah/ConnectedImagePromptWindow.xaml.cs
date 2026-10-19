using System.Windows;

namespace MangaMeYeah;

public partial class ConnectedImagePromptWindow : Window
{
    public bool SuppressFuturePrompts => SuppressPromptCheckBox.IsChecked == true;

    public ConnectedImagePromptWindow(string detectedDescription)
    {
        InitializeComponent();
        MessageTextBlock.Text = $"連結画像（{detectedDescription}）を検出しました。\n連結表示に切り替えて表示しますか？";
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
