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

    private readonly TerminalSurfaceController _terminalController;

    public MainWindow()
    {
        InitializeComponent();
        _terminalController = new TerminalSurfaceController(TerminalSurface);
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        DataContextChanged += MainWindow_DataContextChanged;
    }

    // 仕様書17章：ターミナル画面（RichTextBox）はTerminalSurfaceControllerが描画するため、
    // アクティブなターミナルタブの切り替えに追従して張り替える必要がある。
    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel oldViewModel)
        {
            oldViewModel.TerminalHost.PropertyChanged -= TerminalHost_PropertyChanged;
        }

        if (e.NewValue is MainWindowViewModel newViewModel)
        {
            newViewModel.TerminalHost.PropertyChanged += TerminalHost_PropertyChanged;
            _terminalController.Bind(newViewModel.TerminalHost.ActiveTerminal);
        }
    }

    private void TerminalHost_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalHostViewModel.ActiveTerminal) && sender is TerminalHostViewModel host)
        {
            _terminalController.Bind(host.ActiveTerminal);
        }
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

        // 仕様書9.1章「Ctrl + @」。個別コントロール（TextBox等）がキー入力を消費して
        // Window.InputBindingsまでバブリングしないケースへの保険として、トンネリング段階で
        // 直接コマンドを実行する。ターミナルにフォーカスがある状態でも閉じられるよう、
        // ターミナル用の早期returnより前に判定する。
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

            return;
        }

        // ターミナル画面（仕様書17章）にフォーカスがある間は、ここでキーを横取りしない。
        // トンネリング段階でHandledにするとTextInputイベント自体が発生しなくなり、
        // スペース等の通常入力がターミナルへ届かなくなる（e.OriginalSourceはRichTextBox
        // 内部の要素になることがあるため、型での除外では取りこぼす）。
        if (TerminalSurface.IsKeyboardFocusWithin)
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

            current = VisualTreeUtility.GetParent(current);
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
}
