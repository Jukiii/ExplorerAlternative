using System.Windows;
using System.Windows.Input;

namespace ExplorerAlternative.Views;

public partial class SelectionDialog : Window
{
    public SelectionDialog(string title, string message, IReadOnlyList<string> items)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ItemsListBox.ItemsSource = items;

        if (items.Count > 0)
        {
            ItemsListBox.SelectedIndex = 0;
        }
    }

    public string? SelectedItem { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void ItemsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        SelectedItem = ItemsListBox.SelectedItem as string;
        DialogResult = SelectedItem is not null;
    }
}
