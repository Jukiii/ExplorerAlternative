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

namespace ExplorerAlternative;

public partial class MainWindow : Window
{
    private static readonly string SourcePaneFormat = "ExplorerAlternative.SourcePane";
    private static readonly string FavoriteReorderFormat = "ExplorerAlternative.FavoriteEntry";

    private Point? _tabDragStartPoint;
    private bool _tabDragDuplicated;
    private Point? _fileDragStartPoint;
    private FavoriteEntry? _favoriteDragStartEntry;
    private Point? _favoriteDragStartPoint;
    private InsertionLineAdorner? _favoriteInsertionAdorner;
    private FileSystemNodeViewModel? _dragHoverNode;
    private DispatcherTimer? _dragHoverExpandTimer;

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

    // 仕様書11章：パンくずドロップダウンの左クリック＝パス全体を置換。
    // 仕様書21章「Show Commit」：Git/SVN情報ペインの簡易コミット履歴をクリックすると、
    // そのコミットを選択した状態でLogウィンドウ（変更内容つき）を開く。
    private void CommitLogEntry_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CommitLogEntry entry } element)
        {
            return;
        }

        var current = (DependencyObject)element;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: PaneViewModel pane })
            {
                if (pane.ShowCommitCommand.CanExecute(entry))
                {
                    pane.ShowCommitCommand.Execute(entry);
                }

                break;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        e.Handled = true;
    }

    private void BreadcrumbDropdownItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BreadcrumbDropdownItem item } && item.NavigateCommand.CanExecute(null))
        {
            item.NavigateCommand.Execute(null);
            e.Handled = true;
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

    // 詳細表示の列ヘッダークリック：DisplayMemberBindingのパスから並び替え対象の列を判定する
    // （「名前」列だけはアイコン付きCellTemplateでDisplayMemberBindingを持たないためnull判定）。
    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Column: { } column, DataContext: PaneViewModel pane })
        {
            return;
        }

        var sortKey = column.DisplayMemberBinding switch
        {
            null => "Name",
            Binding { Path.Path: "SizeDisplay" } => "Size",
            Binding { Path.Path: "LastModifiedDisplay" } => "LastModified",
            Binding { Path.Path: "KindDisplay" } => "Kind",
            _ => null,
        };

        if (sortKey is not null && pane.SortByColumnCommand.CanExecute(sortKey))
        {
            pane.SortByColumnCommand.Execute(sortKey);
        }
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
    // ←は、選択中がすでに折りたたみ済み/ファイルの場合は親フォルダを選択し、
    // 展開中のフォルダを選択している場合はそのフォルダを折りたたむ（Windows標準ツリーの挙動）。
    private void TreeListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        NodeListBox_PreviewKeyDown(sender, e);

        if (e.Handled || sender is not ListBox listBox || listBox.DataContext is not PaneViewModel pane)
        {
            return;
        }

        var node = pane.PrimarySelectedNode;
        if (node is null)
        {
            return;
        }

        if (e.Key == Key.Right && node.IsDirectory && !node.IsExpanded)
        {
            node.IsExpanded = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            if (node.IsDirectory && node.IsExpanded)
            {
                node.IsExpanded = false;
                e.Handled = true;
            }
            else
            {
                var parent = FindParentNode(pane.VisibleNodes, node);
                if (parent is not null)
                {
                    SelectTreeNode(listBox, parent);
                    e.Handled = true;
                }
            }
        }
    }

    private static FileSystemNodeViewModel? FindParentNode(IList<FileSystemNodeViewModel> visibleNodes, FileSystemNodeViewModel node)
    {
        var index = visibleNodes.IndexOf(node);
        if (index < 0)
        {
            return null;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (visibleNodes[i].Depth == node.Depth - 1)
            {
                return visibleNodes[i];
            }
        }

        return null;
    }

    private static void SelectTreeNode(ListBox listBox, FileSystemNodeViewModel node)
    {
        listBox.SelectedItem = node;
        listBox.ScrollIntoView(node);

        listBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (listBox.ItemContainerGenerator.ContainerFromItem(node) is ListBoxItem container)
            {
                container.Focus();
            }
        }), DispatcherPriority.ContextIdle);
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

            source = VisualTreeHelper.GetParent(source);
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

    // 仕様書4章：メインペインからフォルダをお気に入りへドラッグ&ドロップして追加する。
    // 仕様書4章：お気に入り欄内での並び替え用ドラッグ開始検出。行の大部分は「移動」ボタンが
    // 占めているため、ファイル一覧のドラッグ検出（NodeListBox_PreviewMouseLeftButtonDown等）と
    // 同様にトンネリング段階で検出する。
    private void FavoritesListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        _favoriteDragStartEntry = IsOverItemContainer(source) ? FindFavoriteFromVisual(source) : null;
        _favoriteDragStartPoint = _favoriteDragStartEntry is not null ? e.GetPosition(null) : null;
    }

    private void FavoritesListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_favoriteDragStartEntry is null || _favoriteDragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(null);
        var diff = _favoriteDragStartPoint.Value - current;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var entry = _favoriteDragStartEntry;
        _favoriteDragStartEntry = null;
        _favoriteDragStartPoint = null;

        if (sender is not ListBox listBox)
        {
            return;
        }

        DragDrop.DoDragDrop(listBox, new DataObject(FavoriteReorderFormat, entry), DragDropEffects.Move);
    }

    private void FavoritesListBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(FavoriteReorderFormat))
        {
            e.Effects = DragDropEffects.Move;

            if (sender is ListBox listBox)
            {
                UpdateFavoriteInsertionLine(listBox, e);
            }
        }
        else
        {
            // 仕様書4章：フォルダをドラッグ&ドロップしてお気に入りに追加。
            // ファイル一覧側のドラッグ開始（NodeListBox_PreviewMouseMove）はCopy|Moveのみを許可しており、
            // ここでLinkを指定すると許可された効果に含まれないためWPFがDropイベントを発火せず、
            // 常にDoDragDropの結果がNoneになってしまう（お気に入りに追加できない不具合の原因）。
            RemoveFavoriteInsertionLine();
            e.Effects = TryGetDroppedFolder(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void FavoritesListBox_DragLeave(object sender, DragEventArgs e)
    {
        RemoveFavoriteInsertionLine();
    }

    private void FavoritesListBox_Drop(object sender, DragEventArgs e)
    {
        RemoveFavoriteInsertionLine();

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // お気に入り内での並び替え（ドラッグ&ドロップ）。
        if (e.Data.GetData(FavoriteReorderFormat) is FavoriteEntry draggedEntry)
        {
            if (sender is ListBox listBox)
            {
                var targetIndex = ResolveFavoriteDropIndex(listBox, e, viewModel.NavigationPane.Favorites, draggedEntry);
                viewModel.NavigationPane.MoveFavoriteToIndex(draggedEntry, targetIndex);
            }

            e.Handled = true;
            return;
        }

        // フォルダをドラッグ&ドロップしてお気に入りへ追加。
        if (TryGetDroppedFolder(e, out var folderPath))
        {
            var name = System.IO.Path.GetFileName(folderPath.TrimEnd('\\'));
            viewModel.NavigationPane.AddFavorite(string.IsNullOrEmpty(name) ? folderPath : name, folderPath);
            e.Handled = true;
        }
    }

    // お気に入り欄の並び替え：カーソルが対象行の上半分/下半分のどちらにあるかで、
    // その行の前/後どちらに挿入するかを決める（挿入線の表示位置とも一致させる）。
    private static (FavoriteEntry? Entry, bool InsertBefore, ListBoxItem? Container) FindFavoriteDropPosition(ListBox listBox, DragEventArgs e)
    {
        var position = e.GetPosition(listBox);
        var hit = VisualTreeHelper.HitTest(listBox, position)?.VisualHit;
        var container = FindAncestor<ListBoxItem>(hit);

        if (container is null || container.DataContext is not FavoriteEntry entry)
        {
            return (null, true, null);
        }

        var topLeft = container.TranslatePoint(new Point(0, 0), listBox);
        var insertBefore = position.Y < topLeft.Y + container.ActualHeight / 2;

        return (entry, insertBefore, container);
    }

    private static int ResolveFavoriteDropIndex(ListBox listBox, DragEventArgs e, ObservableCollection<FavoriteEntry> favorites, FavoriteEntry draggedEntry)
    {
        var (targetEntry, insertBefore, _) = FindFavoriteDropPosition(listBox, e);

        if (targetEntry is null || ReferenceEquals(targetEntry, draggedEntry))
        {
            return favorites.Count - 1;
        }

        var sourceIndex = favorites.IndexOf(draggedEntry);
        var targetIndex = favorites.IndexOf(targetEntry);
        // ObservableCollection.Move()はまず除去してから挿入するため、除去後の並びを基準にした
        // 挿入位置に変換する必要がある（除去元より後ろへ移動する場合は1つ前へ詰める）。
        var desiredIndexBeforeRemoval = insertBefore ? targetIndex : targetIndex + 1;

        return sourceIndex < desiredIndexBeforeRemoval ? desiredIndexBeforeRemoval - 1 : desiredIndexBeforeRemoval;
    }

    // 挿入位置を示す線をAdornerとして描画する。挿入位置が変わるたびに呼び出す。
    private void UpdateFavoriteInsertionLine(ListBox listBox, DragEventArgs e)
    {
        var (_, insertBefore, container) = FindFavoriteDropPosition(listBox, e);

        double lineY;
        if (container is not null)
        {
            var topLeft = container.TranslatePoint(new Point(0, 0), listBox);
            lineY = insertBefore ? topLeft.Y : topLeft.Y + container.ActualHeight;
        }
        else if (listBox.Items.Count > 0 &&
                 listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) is ListBoxItem lastContainer)
        {
            // 項目の外（末尾の余白等）へのドロップ：最後の項目の下に表示する。
            lineY = lastContainer.TranslatePoint(new Point(0, lastContainer.ActualHeight), listBox).Y;
        }
        else
        {
            lineY = 0;
        }

        EnsureFavoriteInsertionAdorner(listBox);
        _favoriteInsertionAdorner?.SetLineY(lineY);
    }

    private void EnsureFavoriteInsertionAdorner(ListBox listBox)
    {
        if (_favoriteInsertionAdorner is not null && ReferenceEquals(_favoriteInsertionAdorner.AdornedElement, listBox))
        {
            return;
        }

        RemoveFavoriteInsertionLine();

        if (AdornerLayer.GetAdornerLayer(listBox) is not { } layer)
        {
            return;
        }

        _favoriteInsertionAdorner = new InsertionLineAdorner(listBox);
        layer.Add(_favoriteInsertionAdorner);
    }

    private void RemoveFavoriteInsertionLine()
    {
        if (_favoriteInsertionAdorner is null)
        {
            return;
        }

        AdornerLayer.GetAdornerLayer((UIElement)_favoriteInsertionAdorner.AdornedElement)?.Remove(_favoriteInsertionAdorner);
        _favoriteInsertionAdorner = null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    // お気に入り欄の並び替え中、挿入位置を示す横線を描画するだけの軽量Adorner。
    private sealed class InsertionLineAdorner : Adorner
    {
        private readonly Pen _pen;
        private double _lineY;

        public InsertionLineAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            var brush = Application.Current?.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            _pen = new Pen(brush, 2);
        }

        public void SetLineY(double y)
        {
            _lineY = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var width = AdornedElement.RenderSize.Width;
            drawingContext.DrawLine(_pen, new Point(0, _lineY), new Point(width, _lineY));
        }
    }

    private static FavoriteEntry? FindFavoriteFromVisual(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: FavoriteEntry entry })
            {
                return entry;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
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

            source = VisualTreeHelper.GetParent(source);
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

    // ターミナル部分をクリックしたときに入力欄へフォーカスする（すぐに入力できるように
    // するため）。ただし出力欄（TerminalOutputTextBox）自体のクリックは、テキスト選択・
    // コピーのための操作である場合があるため対象外とする。ここで無条件にフォーカスを
    // 奪うと、出力欄でのドラッグ選択が毎回入力欄へのフォーカス移動によって
    // キャンセルされてしまい、ターミナルの内容を選択・コピーできなくなる不具合があった。
    private void TerminalPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsDescendantOf(e.OriginalSource as DependencyObject, TerminalOutputTextBox))
        {
            return;
        }

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
        var current = VisualTreeHelper.GetParent(element);

        while (current is not null and not ScrollViewer)
        {
            current = VisualTreeHelper.GetParent(current);
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
