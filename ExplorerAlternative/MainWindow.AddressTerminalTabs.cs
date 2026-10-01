using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerAlternative.Models;
using ExplorerAlternative.ViewModels;
using ExplorerAlternative.Views;

namespace ExplorerAlternative;

/// <summary>
/// MainWindowのうち、アドレスバー・ターミナル・タブ・タグ・ナビゲーションペインの操作。
/// </summary>
public partial class MainWindow
{
    // 仕様書11章：パンくずの空白部分をクリックしたらアドレス編集モードにする。
    // ただし、パンくずセグメントやドロップダウン矢印（Button/ToggleButton）自体のクリックは
    // 従来通りナビゲーション操作として扱い、編集モードへは切り替えない。
    private void AddressBarBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOverButtonOrDropdownItem(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var command = viewModel.ActiveTab?.ActivePane.BeginAddressEditCommand;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    // ButtonBase（パンくずセグメント・ドロップダウン矢印）自体、またはドロップダウン内の
    // 項目（BreadcrumbDropdownItemテンプレートのBorder。Buttonではないため別途判定が必要）の
    // 上かどうかを判定する。
    private static bool IsOverButtonOrDropdownItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            if (source is FrameworkElement { DataContext: BreadcrumbDropdownItem })
            {
                return true;
            }

            source = VisualTreeUtility.GetParent(source);
        }

        return false;
    }

    // 仕様書11章：パンくずドロップダウンを開いた状態で、そのドロップダウン（またはドロップダウン
    // 矢印）以外の場所をクリックしたら閉じる（Windows 11 Explorerと同様の操作感）。
    private void RootWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var pane = viewModel.ActiveTab?.ActivePane;

        // 仕様書11章：アドレス編集中に、編集欄以外の場所をクリックしたら編集を取り消し、
        // 元のパンくず表示に戻す。編集欄自体（カーソル移動等）のクリックは無視する。
        // マウスクリックだけではフォーカスが移動しない要素（空白部分等）も多いため、
        // TextBox.LostFocusだけに頼らずここでも判定する。
        if (pane is { IsAddressEditing: true } &&
            !IsDescendantOf(e.OriginalSource as DependencyObject, AddressEditTextBox) &&
            pane.CancelAddressEditCommand.CanExecute(null))
        {
            pane.CancelAddressEditCommand.Execute(null);
        }

        if (IsOverButtonOrDropdownItem(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var segments = pane?.BreadcrumbSegments;
        if (segments is null)
        {
            return;
        }

        foreach (var segment in segments)
        {
            segment.IsDropdownOpen = false;
        }
    }

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ancestor))
            {
                return true;
            }

            source = VisualTreeUtility.GetParent(source);
        }

        return false;
    }

    private void AddressEditTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || e.NewValue is not true)
        {
            return;
        }

        textBox.Focus();
        textBox.SelectAll();
    }

    // 仕様書11章：アドレス編集中に他の場所をクリックする（＝フォーカスが外れる）と、
    // 編集を確定せず元の表示（パンくず）に戻す。Enter確定時もフォーカスが外れるが、
    // その時点で既にIsAddressEditingはfalseになっているため二重処理にはならない。
    private void AddressEditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PaneViewModel pane })
        {
            return;
        }

        if (pane.IsAddressEditing && pane.CancelAddressEditCommand.CanExecute(null))
        {
            pane.CancelAddressEditCommand.Execute(null);
        }
    }

    // 仕様書17章「高さはドラッグ変更可能」。上へドラッグすると高くなる。
    private void TerminalResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.TerminalHost.PanelHeight -= e.VerticalChange;
        }
    }

    // ターミナル部分をクリックしたときに入力できるようフォーカスする。ターミナル画面自身の
    // クリックはテキスト選択の操作でもあるため、フォーカス移動を横取りしない。
    private void TerminalPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsDescendantOf(e.OriginalSource as DependencyObject, TerminalSurface))
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => TerminalSurface.Focus()), DispatcherPriority.Input);
    }

    // 仕様書17章：タブストリップでの切替。
    private void TerminalTab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalViewModel terminal } &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.TerminalHost.ActiveTerminal = terminal;
            e.Handled = true;
        }
    }

    private void TerminalTabClose_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalViewModel terminal } &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.TerminalHost.CloseTerminalCommand.Execute(terminal);
            e.Handled = true;
        }
    }

    private void PaneContainer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && viewModel.SetActivePaneCommand.CanExecute(pane))
        {
            viewModel.SetActivePaneCommand.Execute(pane);
        }
    }

    // 仕様書18.1章：タブをCtrlキーを押しながらドラッグすると複製する。
    // ドラッグ中のフローティング表示までは行わず、ドラッグ検知の瞬間に複製する簡易実装。
    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStartPoint = e.GetPosition(null);
        _tabDragDuplicated = false;
    }

    private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragDuplicated || _tabDragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var current = e.GetPosition(null);
        var diff = _tabDragStartPoint.Value - current;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not TabViewModel tab)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _tabDragDuplicated = true;
        viewModel.DuplicateTabCommand.Execute(tab);
    }

    private void TabItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _tabDragStartPoint = null;
        _tabDragDuplicated = false;
    }

    /// <summary>仕様書5章：タグ一覧のダブルクリックでアイコン・色の編集ダイアログを開く。</summary>
    private void TagRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 &&
            sender is FrameworkElement { DataContext: TagDefinition tag } &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NavigationPane.EditTagCommand.Execute(tag);
        }
    }

    // 仕様書4章：ナビゲーションペイン内のListBox（お気に入り等）は既定でホイール/トラックパッドの
    // スクロールを自身で消費してしまい、外側のScrollViewer（ペイン全体）へ伝播しない。
    // ただし、MaxHeightで内部スクロールが必要な一覧（最近使った場所等）まで一律に外側へ
    // 転送すると、その一覧自身がスクロールできなくなってしまう。そのため、内側の
    // ScrollViewerがまだその方向へスクロールできる間は内側に処理させ、内側が端まで
    // 達している場合のみ外側のScrollViewer（ペイン全体）へスクロールを転送する。
    private void NavListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var innerScrollViewer = FindDescendantScrollViewer(element);
        if (innerScrollViewer is not null)
        {
            var canScrollInner = e.Delta > 0
                ? innerScrollViewer.VerticalOffset > 0
                : innerScrollViewer.VerticalOffset < innerScrollViewer.ScrollableHeight;
            if (canScrollInner)
            {
                return;
            }
        }

        var scrollViewer = FindAncestorScrollViewer(element);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject element)
    {
        var current = VisualTreeUtility.GetParent(element);

        while (current is not null and not ScrollViewer)
        {
            current = VisualTreeUtility.GetParent(current);
        }

        return current as ScrollViewer;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var found = FindDescendantScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
