using System.Windows;
using System.Windows.Documents;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow()
    {
        InitializeComponent();
    }

    public void SetPreview(PreviewViewModel previewViewModel)
    {
        DataContext = previewViewModel;
        Title = $"プレビュー - {previewViewModel.Title}";
        MarkdownViewer.Document = previewViewModel.MarkdownDocument ?? new FlowDocument();
    }
}
