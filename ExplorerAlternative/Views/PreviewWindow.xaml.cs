using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Views;

public partial class PreviewWindow : Window
{
    private const double MinScale = 0.1;
    private const double MaxScale = 8.0;

    public PreviewWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as PreviewViewModel)?.Closed?.Invoke();
    }

    public void SetPreview(PreviewViewModel previewViewModel)
    {
        DataContext = previewViewModel;
        Title = $"プレビュー - {previewViewModel.Title}";
        MarkdownViewer.Document = previewViewModel.MarkdownDocument ?? new FlowDocument();
        ImageScaleTransform.ScaleX = 1;
        ImageScaleTransform.ScaleY = 1;
        previewViewModel.RequestJumpToLine = JumpToLine;

        // 仕様書13章「PDF」：PDFを読み込んで、ページの場所が決まったら、見えている付近を描く。
        PdfScrollViewer.ScrollToTop();
        previewViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewViewModel.PdfPageCount) && ReferenceEquals(DataContext, previewViewModel))
            {
                Dispatcher.BeginInvoke(new Action(UpdatePdfVisibleRange), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
    }

    private void PdfScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdatePdfVisibleRange();

    private void PdfScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePdfVisibleRange();

    // 仕様書13章「PDF」：各ページの、表示領域の中での位置を集めて、見えているページの範囲と、いまのページ番号を
    // ViewModelへ伝える（計算は、PdfScrollCalculator。画像を描くのは、ViewModelの役目。Viewは、位置を集めるだけ）。
    private void UpdatePdfVisibleRange()
    {
        if (DataContext is not PreviewViewModel { Kind: PreviewKind.Pdf } viewModel || viewModel.PdfPages.Count == 0)
        {
            return;
        }

        var viewportHeight = PdfScrollViewer.ViewportHeight;
        var bounds = new List<(double Top, double Bottom)?>(viewModel.PdfPages.Count);

        for (var i = 0; i < viewModel.PdfPages.Count; i++)
        {
            if (PdfPagesControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
            {
                bounds.Add(null);
                continue;
            }

            var top = container.TranslatePoint(new Point(0, 0), PdfScrollViewer).Y;
            bounds.Add((top, top + container.ActualHeight));

            // 表示領域より下のページは、位置を調べる必要がない。
            if (top >= viewportHeight)
            {
                break;
            }
        }

        if (PdfScrollCalculator.Compute(bounds, viewportHeight) is not { } range)
        {
            return;
        }

        viewModel.SetCurrentPdfPage(range.CurrentPage + 1);
        _ = viewModel.UpdatePdfVisibleRangeAsync(range.FirstVisible, range.LastVisible);
    }

    // 仕様書16章「シンボルクリックで該当位置へジャンプ」。
    private void JumpToLine(int line)
    {
        var lineIndex = Math.Max(0, line - 1);
        TextViewer.UpdateLayout();

        var charIndex = TextViewer.GetCharacterIndexFromLineIndex(lineIndex);
        if (charIndex < 0)
        {
            return;
        }

        TextViewer.Focus();
        TextViewer.Select(charIndex, 0);
        TextViewer.ScrollToLine(lineIndex);
    }

    // 仕様書13章：← / → で前後移動、Escで閉じる。
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not PreviewViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right:
                viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;

            // 仕様書13章「PDF」：PDFのときだけ、PageUp/PageDown（とHome/End）で、1画面分ずつスクロールする。
            case Key.PageUp when viewModel.Kind == PreviewKind.Pdf:
                PdfScrollViewer.ScrollToVerticalOffset(PdfScrollViewer.VerticalOffset - (PdfScrollViewer.ViewportHeight * 0.9));
                e.Handled = true;
                break;

            case Key.PageDown when viewModel.Kind == PreviewKind.Pdf:
                PdfScrollViewer.ScrollToVerticalOffset(PdfScrollViewer.VerticalOffset + (PdfScrollViewer.ViewportHeight * 0.9));
                e.Handled = true;
                break;

            case Key.Home when viewModel.Kind == PreviewKind.Pdf:
                PdfScrollViewer.ScrollToTop();
                e.Handled = true;
                break;

            case Key.End when viewModel.Kind == PreviewKind.Pdf:
                PdfScrollViewer.ScrollToBottom();
                e.Handled = true;
                break;

            case Key.Escape:
                viewModel.RequestClose?.Invoke();
                e.Handled = true;
                break;
        }
    }

    // 仕様書13章：画像プレビューのズーム。
    private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not PreviewViewModel { Kind: PreviewKind.Image })
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = ImageScaleTransform.ScaleX * factor;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);

        ImageScaleTransform.ScaleX = newScale;
        ImageScaleTransform.ScaleY = newScale;
        e.Handled = true;
    }

    // 仕様書13章「画像ズーム/パン」：ドラッグでスクロール位置を動かす（拡大時に全体を確認できるように）。
    private Point? _imagePanStart;
    private double _imagePanStartHorizontalOffset;
    private double _imagePanStartVerticalOffset;

    private void ImageScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PreviewViewModel { Kind: PreviewKind.Image })
        {
            return;
        }

        _imagePanStart = e.GetPosition(ImageScrollViewer);
        _imagePanStartHorizontalOffset = ImageScrollViewer.HorizontalOffset;
        _imagePanStartVerticalOffset = ImageScrollViewer.VerticalOffset;
        ImageScrollViewer.CaptureMouse();
    }

    private void ImageScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_imagePanStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ImageScrollViewer);
        var offsetX = current.X - start.X;
        var offsetY = current.Y - start.Y;

        ImageScrollViewer.ScrollToHorizontalOffset(_imagePanStartHorizontalOffset - offsetX);
        ImageScrollViewer.ScrollToVerticalOffset(_imagePanStartVerticalOffset - offsetY);
    }

    private void ImageScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _imagePanStart = null;
        ImageScrollViewer.ReleaseMouseCapture();
    }
}
