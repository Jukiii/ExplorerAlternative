using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative;

public partial class MainWindow : Window
{
    private static readonly string SourcePaneFormat = "ExplorerAlternative.SourcePane";

    private Point? _tabDragStartPoint;
    private bool _tabDragDuplicated;
    private Point? _fileDragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
    }

    // 仕様書40章：システムトレイに常駐中は、ウィンドウを閉じてもアプリを終了せずトレイへ格納する。
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel { ShouldHideToTrayOnClose: true })
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // ListBox(Extended選択モード)はSpaceキーを選択切り替えとして内部消費し、
        // Window.InputBindingsのKeyBindingまでバブリングしないため、
        // トンネリング段階(PreviewKeyDown)でプレビューコマンドを直接実行する。
        if (e.Key == Key.Space && e.OriginalSource is not TextBox)
        {
            e.Handled = true;

            if (viewModel.TogglePreviewCommand.CanExecute(null))
            {
                viewModel.TogglePreviewCommand.Execute(null);
            }

            return;
        }

        // 仕様書9.1章「Ctrl + @」。個別コントロール（TextBox等）がキー入力を消費して
        // Window.InputBindingsまでバブリングしないケースへの保険として、トンネリング段階で
        // 直接コマンドを実行する（Spaceキーと同じ対策）。
        var isTerminalToggleGesture =
            (e.Key == Key.OemTilde && Keyboard.Modifiers == ModifierKeys.Control) ||
            (e.Key == Key.D2 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift));

        if (isTerminalToggleGesture)
        {
            e.Handled = true;

            if (viewModel.ToggleTerminalCommand.CanExecute(null))
            {
                viewModel.ToggleTerminalCommand.Execute(null);
            }
        }
    }

    // 仕様書11章：パンくずドロップダウンの右クリック＝部分パス置換（下層を維持）。
    private void BreadcrumbDropdownItem_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BreadcrumbDropdownItem item } && item.NavigatePartialCommand.CanExecute(null))
        {
            item.NavigatePartialCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TreeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.DataContext is not PaneViewModel pane)
        {
            return;
        }

        pane.UpdateSelection(listBox.SelectedItems.Cast<FileSystemNodeViewModel>());
    }

    private void NodeListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        if (pane.OpenCommand.CanExecute(null))
        {
            pane.OpenCommand.Execute(null);
        }
    }

    // 仕様書7章：Enter(開く)/F2(名前変更)/Delete(削除) は階層表示・詳細表示の両方で共通。
    private void NodeListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when pane.OpenCommand.CanExecute(null):
                pane.OpenCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F2 when pane.RenameCommand.CanExecute(null):
                pane.RenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when pane.DeleteCommand.CanExecute(null):
                pane.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // 仕様書7章：階層表示のみ →(展開)/←(折りたたみ) に対応。
    private void TreeListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        NodeListBox_PreviewKeyDown(sender, e);

        if (e.Handled || sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane)
        {
            return;
        }

        var node = pane.PrimarySelectedNode;
        if (node is null || !node.IsDirectory)
        {
            return;
        }

        if (e.Key == Key.Right && !node.IsExpanded)
        {
            node.IsExpanded = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Left && node.IsExpanded)
        {
            node.IsExpanded = false;
            e.Handled = true;
        }
    }

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

        DragDrop.DoDragDrop(element, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private static bool IsOverItemContainer(DependencyObject? source)
    {
        while (source is not null and not ListBox and not ListView)
        {
            if (source is ListBoxItem or ListViewItem)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void PaneGrid_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel pane ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var sourcePaths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var destinationFolder = ResolveDropTargetFolder(e, element, pane);

        e.Effects = DetermineDropEffect(sourcePaths, destinationFolder, e);
        e.Handled = true;
    }

    private void PaneGrid_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PaneViewModel destinationPane ||
            !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var sourcePaths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var destinationFolder = ResolveDropTargetFolder(e, element, destinationPane);
        var effects = DetermineDropEffect(sourcePaths, destinationFolder, e);

        if (effects == DragDropEffects.None)
        {
            return;
        }

        var isMove = effects == DragDropEffects.Move;
        var sourcePane = e.Data.GetDataPresent(SourcePaneFormat) ? e.Data.GetData(SourcePaneFormat) as PaneViewModel : null;

        destinationPane.DropFiles(sourcePaths, destinationFolder, isMove);

        if (isMove && sourcePane is not null && !ReferenceEquals(sourcePane, destinationPane))
        {
            sourcePane.RefreshCurrentFolder();
        }

        e.Handled = true;
    }

    // ドロップ先がフォルダ行であればそのフォルダの中へ、それ以外（ファイル行や空白部分）は
    // ペインの現在フォルダへドロップしたものとして扱う。
    private static string ResolveDropTargetFolder(DragEventArgs e, FrameworkElement relativeTo, PaneViewModel pane)
    {
        var position = e.GetPosition(relativeTo);
        var hit = VisualTreeHelper.HitTest(relativeTo, position)?.VisualHit;
        var node = FindNodeFromVisual(hit);

        return node is { IsDirectory: true } ? node.FullPath : pane.CurrentPath;
    }

    private static FileSystemNodeViewModel? FindNodeFromVisual(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: FileSystemNodeViewModel node })
            {
                return node;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    // Ctrl=コピー、Shift=移動、指定なしは同一ドライブなら移動・異なるドライブならコピー
    // （Windows標準エクスプローラーの慣習に合わせる）。
    private static DragDropEffects DetermineDropEffect(IReadOnlyList<string> sourcePaths, string destinationFolder, DragEventArgs e)
    {
        if (sourcePaths.Count == 0)
        {
            return DragDropEffects.None;
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

    // 仕様書4章：メインペインからフォルダをお気に入りへドラッグ&ドロップして追加する。
    private void FavoritesListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedFolder(e, out _) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void FavoritesListBox_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedFolder(e, out var folderPath) || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var name = System.IO.Path.GetFileName(folderPath.TrimEnd('\\'));
        viewModel.NavigationPane.AddFavorite(string.IsNullOrEmpty(name) ? folderPath : name, folderPath);
        e.Handled = true;
    }

    private static bool TryGetDroppedFolder(DragEventArgs e, out string folderPath)
    {
        folderPath = string.Empty;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var folder = paths.FirstOrDefault(System.IO.Directory.Exists);

        if (folder is null)
        {
            return false;
        }

        folderPath = folder;
        return true;
    }

    // 仕様書11章：パンくずの空白部分をクリックしたらアドレス編集モードにする。
    // ただし、パンくずセグメントやドロップダウン矢印（Button/ToggleButton）自体のクリックは
    // 従来通りナビゲーション操作として扱い、編集モードへは切り替えない。
    private void AddressBarBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOverButton(e.OriginalSource as DependencyObject))
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

    private static bool IsOverButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
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

    private void TerminalOutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        (sender as TextBox)?.ScrollToEnd();
    }

    // ターミナルを表示した際、すぐに入力できるよう入力欄へフォーカスする。
    private void TerminalInputTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || e.NewValue is not true)
        {
            return;
        }

        textBox.Focus();
    }

    // ターミナル部分（出力欄を含む）をクリックしたときに入力欄へフォーカスする。
    // 出力欄（TextBox）自身がクリック時に自分へフォーカスを奪う処理を持っているため、
    // 同じ入力処理の中でFocus()を呼んでもすぐに上書きされてしまう。
    // Dispatcher.BeginInvokeで、クリックの既定処理が完了した後に改めてフォーカスする。
    private void TerminalPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => TerminalInputTextBox.Focus()), DispatcherPriority.Input);
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

    // 仕様書19章：ファイル・フォルダをターミナル入力欄へドラッグ＆ドロップするとパスが入力される。
    private void TerminalInput_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TerminalInput_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var terminal = viewModel.TerminalHost.ActiveTerminal;

        if (terminal is null)
        {
            return;
        }

        foreach (var path in paths)
        {
            terminal.InsertPathIntoInput(path);
        }

        e.Handled = true;
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

    // 仕様書4章：ナビゲーションペイン内のListBox（お気に入り等）は既定でホイール/トラックパッドの
    // スクロールを自身で消費してしまい、外側のScrollViewer（ペイン全体）へ伝播しない。
    // ここで明示的に外側のScrollViewerへスクロールを転送する。
    private void NavListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
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
        var current = VisualTreeHelper.GetParent(element);

        while (current is not null and not ScrollViewer)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as ScrollViewer;
    }
}
