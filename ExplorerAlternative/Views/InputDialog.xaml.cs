using System.Windows;

namespace ExplorerAlternative.Views;

public partial class InputDialog : Window
{
    public InputDialog(string title, string message, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ValueTextBox.Text = defaultValue;
        ValueTextBox.SelectAll();
        Loaded += (_, _) => ValueTextBox.Focus();
    }

    public string InputText { get; private set; } = string.Empty;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        InputText = ValueTextBox.Text;
        DialogResult = true;
    }
}
