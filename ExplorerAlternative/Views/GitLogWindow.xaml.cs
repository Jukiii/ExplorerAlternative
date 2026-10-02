using System.Windows;
using System.Windows.Documents;
using ExplorerAlternative.Rendering;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class GitLogWindow : Window
{
    public GitLogWindow(GitLogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Title = viewModel.Title;

        // 選んだコミットの変更内容を、追加行（+）は黄緑、削除行（-）は赤の背景で表示する。
        void ShowDiff() => DiffViewer.Document = CreateDiffDocument(viewModel.DiffText);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GitLogViewModel.DiffText))
            {
                ShowDiff();
            }
        };
        ShowDiff();
    }

    private FlowDocument CreateDiffDocument(string diffText)
    {
        var document = PatchDiffRenderer.Render(diffText);
        document.PageWidth = 100_000; // 折り返さず、横にスクロールする（長い行を、そのまま見られるように）
        return document;
    }
}
