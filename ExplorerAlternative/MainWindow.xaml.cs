using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerAlternative.Models;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative;

public partial class MainWindow : Window
{
    private static readonly string SourcePaneFormat = "ExplorerAlternative.SourcePane";
    private static readonly string FavoriteReorderFormat = "ExplorerAlternative.FavoriteEntry";

    // トラックパッドの横スワイプはWM_MOUSEHWHEELとしてOSから送られてくるが、WPFの
    // ScrollViewerは既定でこれをハンドリングしない（縦方向のWM_MOUSEWHEELのみ対応）ため、
    // ウィンドウメッセージを直接フックしてカーソル位置直下のScrollViewerへ手動で反映する。
    private const int WM_MOUSEHWHEEL = 0x020E;

    private Point? _tabDragStartPoint;
    private bool _tabDragDuplicated;
    private Point? _fileDragStartPoint;
    private FavoriteEntry? _favoriteDragStartEntry;
    private Point? _favoriteDragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(HorizontalScrollWndProc);
        }
    }

    private IntPtr HorizontalScrollWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL)
        {
            return IntPtr.Zero;
        }

        var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
        var screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

        var point = PointFromScreen(new Point(screenX, screenY));
        var scrollViewer = FindScrollViewerAt(point);
        if (scrollViewer is null)
        {
            return IntPtr.Zero;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta / 3.0);
        handled = true;
        return IntPtr.Zero;
    }

    // カーソル直下から祖先方向へScrollViewerを探す際、最初に見つかったものをそのまま
    // 使うと、ナビゲーションペイン内のListBoxが持つ内部ScrollViewer（既定では横スクロール
    // 無効）に当たってしまい、ペイン全体を横スクロールさせたいケース（ペインをGridSplitter
    // で狭めた場合）で何も起きなくなる。横方向に実際にスクロール可能な最も内側の
    // ScrollViewerを優先し、見つからなければ最初に見つかったものにフォールバックする。
    private ScrollViewer? FindScrollViewerAt(Point point)
    {
        var hit = VisualTreeHelper.HitTest(this, point)?.VisualHit;
        ScrollViewer? fallback = null;
        while (hit is not null)
        {
            if (hit is ScrollViewer scrollViewer)
            {
                if (scrollViewer.ScrollableWidth > 0)
                {
                    return scrollViewer;
                }

                fallback ??= scrollViewer;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return fallback;
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
        }
        else
        {
            // 仕様書4章：フォルダをドラッグ&ドロップしてお気に入りに追加。
            // ファイル一覧側のドラッグ開始（NodeListBox_PreviewMouseMove）はCopy|Moveのみを許可しており、
            // ここでLinkを指定すると許可された効果に含まれないためWPFがDropイベントを発火せず、
            // 常にDoDragDropの結果がNoneになってしまう（お気に入りに追加できない不具合の原因）。
            e.Effects = TryGetDroppedFolder(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void FavoritesListBox_Drop(object sender, DragEventArgs e)
    {
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

    private static int ResolveFavoriteDropIndex(ListBox listBox, DragEventArgs e, ObservableCollection<FavoriteEntry> favorites, FavoriteEntry draggedEntry)
    {
        var position = e.GetPosition(listBox);
        var hit = VisualTreeHelper.HitTest(listBox, position)?.VisualHit;
        var targetEntry = FindFavoriteFromVisual(hit);

        if (targetEntry is null || ReferenceEquals(targetEntry, draggedEntry))
        {
            return favorites.Count - 1;
        }

        return favorites.IndexOf(targetEntry);
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
