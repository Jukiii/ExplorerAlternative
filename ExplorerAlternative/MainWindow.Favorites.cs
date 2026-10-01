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
/// MainWindowのうち、お気に入りのドラッグ&ドロップによる並べ替え・追加（仕様書6章）。
/// </summary>
public partial class MainWindow
{
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

            source = VisualTreeUtility.GetParent(source);
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

            source = VisualTreeUtility.GetParent(source);
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
}
