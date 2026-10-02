using System.Windows;

namespace ExplorerAlternative.Views;

/// <summary>パスフレーズ・パスワードを、伏せ字で尋ねる小さなダイアログ（入力した内容は保存しない）。</summary>
public partial class PasswordPromptDialog : Window
{
    public PasswordPromptDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Loaded += (_, _) => PasswordBoxControl.Focus();
    }

    public string Password { get; private set; } = string.Empty;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Password = PasswordBoxControl.Password;
        DialogResult = true;
    }
}
