using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Views;

/// <summary>
/// 分割ペイン（仕様書19章）の配置パネル。ペインが2つのときは、境界（スプリッター）をドラッグして
/// 2つのペインの比率を変えられる。ペインが1つのときは、全体に広げる。
///
/// <c>ItemsControl</c>のItemsPanelとして使う（ペインはItemsControlが生成する子要素）。境界は
/// 子要素ではなく、このパネル自身が描画して、マウス操作を受け取る（子要素を追加すると、
/// ItemsControlの項目の管理とずれるため）。比率は<see cref="Ratio"/>で、タブの
/// <c>SplitRatio</c>と双方向にバインドして、ワークスペースに保存する。
/// </summary>
public sealed class SplitPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(SplitPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio),
        typeof(double),
        typeof(SplitPanel),
        new FrameworkPropertyMetadata(
            SplitLayout.DefaultRatio,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null,
            (_, value) => SplitLayout.ClampRatio((double)value)));

    public static readonly DependencyProperty SplitterBrushProperty = DependencyProperty.Register(
        nameof(SplitterBrush),
        typeof(Brush),
        typeof(SplitPanel),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _isDragging;

    /// <summary>Horizontalは左右に並べる（境界は縦線）、Verticalは上下に並べる（境界は横線）。</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>最初のペインが占める割合（境界を除いた長さに対して。0.1〜0.9）。</summary>
    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public Brush SplitterBrush
    {
        get => (Brush)GetValue(SplitterBrushProperty);
        set => SetValue(SplitterBrushProperty, value);
    }

    private bool HasSplitter => InternalChildren.Count >= 2;

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0)
        {
            return new Size(0, 0);
        }

        if (!HasSplitter)
        {
            InternalChildren[0].Measure(availableSize);
            return availableSize;
        }

        var total = IsHorizontal ? availableSize.Width : availableSize.Height;
        var layout = SplitLayout.Calculate(total, Ratio);

        Size SizeFor(double length) => IsHorizontal
            ? new Size(length, availableSize.Height)
            : new Size(availableSize.Width, length);

        InternalChildren[0].Measure(SizeFor(layout.First));
        InternalChildren[1].Measure(SizeFor(layout.Second));

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
        {
            return finalSize;
        }

        if (!HasSplitter)
        {
            InternalChildren[0].Arrange(new Rect(finalSize));
            return finalSize;
        }

        var total = IsHorizontal ? finalSize.Width : finalSize.Height;
        var layout = SplitLayout.Calculate(total, Ratio);

        if (IsHorizontal)
        {
            InternalChildren[0].Arrange(new Rect(0, 0, layout.First, finalSize.Height));
            InternalChildren[1].Arrange(new Rect(layout.SecondOffset, 0, layout.Second, finalSize.Height));
        }
        else
        {
            InternalChildren[0].Arrange(new Rect(0, 0, finalSize.Width, layout.First));
            InternalChildren[1].Arrange(new Rect(0, layout.SecondOffset, finalSize.Width, layout.Second));
        }

        return finalSize;
    }

    // 境界を描く。描画しておくことで、この領域がマウス操作の対象（ヒットテスト）にもなる。
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!HasSplitter)
        {
            return;
        }

        var total = IsHorizontal ? ActualWidth : ActualHeight;
        var layout = SplitLayout.Calculate(total, Ratio);

        var rect = IsHorizontal
            ? new Rect(layout.SplitterOffset, 0, SplitLayout.SplitterThickness, ActualHeight)
            : new Rect(0, layout.SplitterOffset, ActualWidth, SplitLayout.SplitterThickness);

        drawingContext.DrawRectangle(SplitterBrush, null, rect);
    }

    private double AxisPosition(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        return IsHorizontal ? point.X : point.Y;
    }

    private double AxisLength => IsHorizontal ? ActualWidth : ActualHeight;

    private bool IsOverSplitter(MouseEventArgs e) =>
        HasSplitter && SplitLayout.IsOnSplitter(AxisPosition(e), AxisLength, Ratio);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isDragging)
        {
            Ratio = SplitLayout.RatioFromPosition(AxisPosition(e), AxisLength);
            e.Handled = true;
            return;
        }

        // 境界の上でだけ、リサイズのカーソルにする（それ以外は、子要素のカーソルに任せる）。
        Cursor = IsOverSplitter(e) ? (IsHorizontal ? Cursors.SizeWE : Cursors.SizeNS) : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!IsOverSplitter(e))
        {
            return;
        }

        // ダブルクリックで、半分ずつに戻す。
        if (e.ClickCount == 2)
        {
            Ratio = SplitLayout.DefaultRatio;
            e.Handled = true;
            return;
        }

        _isDragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDragging = false;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (!_isDragging)
        {
            Cursor = null;
        }
    }
}
