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
/// MainWindowのうち、ファイル一覧のドラッグ&ドロップ（仕様書20章）。
/// </summary>
public partial class MainWindow
{
    // 仕様書20章：ファイル/フォルダのドラッグ&ドロップによる移動・コピー。
    // ドラッグ開始位置が実際の行（ListBoxItem/ListViewItem）上でなければ無視する
    // （空白部分でのマウスドラッグは範囲選択として動作させるため）。
    private void NodeListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragStartPoint = IsOverItemContainer(e.OriginalSource as DependencyObject)
            ? e.GetPosition(null)
            : null;
    }

    private void NodeListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_fileDragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(null);
        var diff = _fileDragStartPoint.Value - current;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _fileDragStartPoint = null;

        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane ||
            pane.SelectedNodes.Count == 0)
        {
            return;
        }

        var fileList = new StringCollection();
        fileList.AddRange(pane.SelectedNodes.Select(n => n.FullPath).ToArray());

        var dataObject = new DataObject();
        dataObject.SetFileDropList(fileList);
        dataObject.SetData(SourcePaneFormat, pane);

        // Link（Alt+ドラッグ＝ショートカット作成）も許可しておかないと、DragOver側でLinkを
        // 要求した際にドロップ先の許可効果と一致せずDropが一切発火しなくなる（WPFの既知の挙動）。
        DragDrop.DoDragDrop(element, dataObject, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private static bool IsOverItemContainer(DependencyObject? source)
    {
        while (source is not null and not ListBox and not ListView)
        {
            if (source is ListBoxItem or ListViewItem)
            {
                return true;
            }

            source = VisualTreeUtility.GetParent(source);
        }

        return false;
    }

    // 仕様書19章「TerminalからExplorerへのドラッグ」：ターミナルで選んだ文字列（パス）のドロップ。
    // ファイルのドロップ（FileDrop）ではない場合だけ、文字列として扱う。
    private static string? GetDroppedText(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            return data.GetData(DataFormats.UnicodeText) as string;
        }

        return data.GetDataPresent(DataFormats.Text) ? data.GetData(DataFormats.Text) as string : null;
    }

    private void PaneGrid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaneViewModel textPane } && GetDroppedText(e.Data) is { } droppedText)
        {
            e.Effects = textPane.CanNavigateToDroppedPath(droppedText) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            ClearDragHoverState();
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ClearDragHoverState();
            return;
        }

        var sourcePaths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var hoveredNode = FindNodeFromVisual(VisualTreeHelper.HitTest(element, e.GetPosition(element))?.VisualHit);
        UpdateDragHoverState(hoveredNode);

        var destinationFolder = ResolveDropTargetFolder(hoveredNode, pane);

        e.Effects = DetermineDropEffect(sourcePaths, destinationFolder, e);
        e.Handled = true;
    }

    private void PaneGrid_DragLeave(object sender, DragEventArgs e)
    {
        ClearDragHoverState();
    }

    private void PaneGrid_Drop(object sender, DragEventArgs e)
    {
        ClearDragHoverState();

        if (sender is FrameworkElement { DataContext: PaneViewModel textPane } && GetDroppedText(e.Data) is { } droppedText)
        {
            textPane.NavigateToDroppedPath(droppedText);
            e.Handled = true;
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel destinationPane ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var sourcePaths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var hoveredNode = FindNodeFromVisual(VisualTreeHelper.HitTest(element, e.GetPosition(element))?.VisualHit);
        var destinationFolder = ResolveDropTargetFolder(hoveredNode, destinationPane);
        var effects = DetermineDropEffect(sourcePaths, destinationFolder, e);

        if (effects == DragDropEffects.None)
        {
            return;
        }

        var isMove = effects == DragDropEffects.Move;
        var sourcePane = e.Data.GetDataPresent(SourcePaneFormat) ? e.Data.GetData(SourcePaneFormat) as PaneViewModel : null;

        if (effects == DragDropEffects.Link)
        {
            destinationPane.DropFilesAsShortcuts(sourcePaths, destinationFolder);
        }
        else if (isMove)
        {
            destinationPane.DropFiles(sourcePaths, destinationFolder, isMove: true);
        }
        else
        {
            destinationPane.DropFilesAsCopy(sourcePaths, destinationFolder);
        }

        // 仕様書26章：DropFilesは実際の移動をファイル操作キュー（バックグラウンド）へ委譲するため
        // 非同期。ここでのRefreshCurrentFolder()は移動完了前に呼ばれる可能性があるが、完了後は
        // 移動元フォルダのFileSystemWatcher（20章・64章）が自動的に再読み込みするため、最終的な
        // 表示状態は正しくなる。
        if (isMove && sourcePane is not null && !ReferenceEquals(sourcePane, destinationPane))
        {
            sourcePane.RefreshCurrentFolder();
        }

        e.Handled = true;
    }

    // 階層表示でフォルダの上に一定時間とどまると、Windows標準Explorerと同様に自動的に
    // 展開する。ホバー先が変わるたびに、直前のホバー対象のハイライトとタイマーをリセットする。
    private void UpdateDragHoverState(FileSystemNodeViewModel? hoveredNode)
    {
        if (ReferenceEquals(_dragHoverNode, hoveredNode))
        {
            return;
        }

        if (_dragHoverNode is not null)
        {
            _dragHoverNode.IsDropTarget = false;
        }

        _dragHoverExpandTimer?.Stop();
        _dragHoverNode = hoveredNode;

        if (hoveredNode is not { IsDirectory: true })
        {
            return;
        }

        hoveredNode.IsDropTarget = true;

        if (hoveredNode.IsExpanded)
        {
            return;
        }

        _dragHoverExpandTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _dragHoverExpandTimer.Tick -= DragHoverExpandTimer_Tick;
        _dragHoverExpandTimer.Tick += DragHoverExpandTimer_Tick;
        _dragHoverExpandTimer.Stop();
        _dragHoverExpandTimer.Start();
    }

    private void DragHoverExpandTimer_Tick(object? sender, EventArgs e)
    {
        _dragHoverExpandTimer?.Stop();

        if (_dragHoverNode is { IsDirectory: true, IsExpanded: false } node)
        {
            node.IsExpanded = true;
        }
    }

    private void ClearDragHoverState()
    {
        _dragHoverExpandTimer?.Stop();

        if (_dragHoverNode is null)
        {
            return;
        }

        _dragHoverNode.IsDropTarget = false;
        _dragHoverNode = null;
    }

    // ドロップ先がフォルダ行であればそのフォルダの中へ、それ以外（ファイル行や空白部分）は
    // ペインの現在フォルダへドロップしたものとして扱う。
    private static string ResolveDropTargetFolder(FileSystemNodeViewModel? hoveredNode, PaneViewModel pane)
    {
        return hoveredNode is { IsDirectory: true } ? hoveredNode.FullPath : pane.CurrentPath;
    }

    private static FileSystemNodeViewModel? FindNodeFromVisual(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: FileSystemNodeViewModel node })
            {
                return node;
            }

            source = VisualTreeUtility.GetParent(source);
        }

        return null;
    }

    // Alt=ショートカット作成、Ctrl=コピー、Shift=移動、指定なしは同一ドライブなら移動・
    // 異なるドライブならコピー（Windows標準エクスプローラーの慣習に合わせる）。
    private static DragDropEffects DetermineDropEffect(IReadOnlyList<string> sourcePaths, string destinationFolder, DragEventArgs e)
    {
        if (sourcePaths.Count == 0)
        {
            return DragDropEffects.None;
        }

        if ((e.KeyStates & DragDropKeyStates.AltKey) != 0)
        {
            return DragDropEffects.Link;
        }

        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
        {
            return DragDropEffects.Copy;
        }

        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0)
        {
            return DragDropEffects.Move;
        }

        var destinationRoot = System.IO.Path.GetPathRoot(destinationFolder);
        var sameDrive = string.Equals(System.IO.Path.GetPathRoot(sourcePaths[0]), destinationRoot, StringComparison.OrdinalIgnoreCase);

        return sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
    }
}
