using System.Windows;
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
