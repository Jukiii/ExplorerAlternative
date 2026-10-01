using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// PaneViewModelのうち、階層表示の展開・折りたたみ、展開状態の保存・復元、並べ替え（仕様書4・6・43章）。
/// </summary>
public sealed partial class PaneViewModel
{
    private void RebuildVisibleNodes()
    {
        VisibleNodes.Clear();
        AppendVisible(RootNodes);
    }

    private void AppendVisible(IEnumerable<FileSystemNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            VisibleNodes.Add(node);

            if (node.IsDirectory && node.IsExpanded && node.Children is not null)
            {
                AppendVisible(node.Children);
            }
        }
    }

    /// <summary>
    /// フォルダの展開・折りたたみ時に呼ばれる。VisibleNodes全体をClear+再構築すると
    /// ListBoxの全コンテナが作り直され、選択位置とスクロール位置が先頭に戻ってしまう
    /// ため、変化があった部分だけを挿入・削除する。
    /// </summary>
    private void OnNodeToggled(FileSystemNodeViewModel node)
    {
        if (_suppressNodeToggleRebuild)
        {
            return;
        }

        var index = VisibleNodes.IndexOf(node);
        if (index < 0)
        {
            RebuildVisibleNodes();
            return;
        }

        if (node.IsExpanded)
        {
            if (node.Children is null)
            {
                return;
            }

            var toInsert = new List<FileSystemNodeViewModel>();
            CollectVisible(node.Children, toInsert);

            for (var i = 0; i < toInsert.Count; i++)
            {
                VisibleNodes.Insert(index + 1 + i, toInsert[i]);
            }
        }
        else
        {
            var removeCount = 0;
            while (index + 1 + removeCount < VisibleNodes.Count && VisibleNodes[index + 1 + removeCount].Depth > node.Depth)
            {
                removeCount++;
            }

            for (var i = 0; i < removeCount; i++)
            {
                VisibleNodes.RemoveAt(index + 1);
            }
        }
    }

    private void CollectVisible(IEnumerable<FileSystemNodeViewModel> nodes, List<FileSystemNodeViewModel> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node);

            if (node.IsDirectory && node.IsExpanded && node.Children is not null)
            {
                CollectVisible(node.Children, result);
            }
        }
    }

    private readonly record struct NodeState(bool IsExpanded, bool IsSelected);

    /// <summary>仕様書43章：ワークスペースへ保存する、階層表示で展開しているフォルダのフルパス。</summary>
    public IReadOnlyList<string> GetExpandedFolderPaths()
    {
        var state = new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);
        CollectNodeState(RootNodes, state);

        return state.Where(pair => pair.Value.IsExpanded).Select(pair => pair.Key).ToList();
    }

    /// <summary>
    /// 仕様書43章：保存しておいた展開状態を復元する。現在のフォルダの下に無いパス（消えたフォルダなど）は無視する。
    /// 展開済みフォルダ1件ごとに表示リストを再構築すると遅くなるため、復元後に1回だけ再構築する。
    /// </summary>
    public void RestoreExpandedFolders(IEnumerable<string> expandedPaths)
    {
        var state = new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in expandedPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            state[path] = new NodeState(IsExpanded: true, IsSelected: false);
        }

        if (state.Count == 0)
        {
            return;
        }

        _suppressNodeToggleRebuild = true;
        try
        {
            ApplyNodeState(RootNodes, state);
        }
        finally
        {
            _suppressNodeToggleRebuild = false;
        }

        RebuildVisibleNodes();
    }

    /// <summary>再読み込み前の選択・展開状態をフルパスで記録する（展開済みの子孫も再帰的に対象）。</summary>
    private static void CollectNodeState(IEnumerable<FileSystemNodeViewModel> nodes, Dictionary<string, NodeState> result)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded || node.IsSelected)
            {
                result[node.FullPath] = new NodeState(node.IsExpanded, node.IsSelected);
            }

            if (node.Children is not null)
            {
                CollectNodeState(node.Children, result);
            }
        }
    }

    /// <summary>再読み込み後の新しいノードに、記録しておいた選択・展開状態をパス一致で復元する。</summary>
    private static void ApplyNodeState(IEnumerable<FileSystemNodeViewModel> nodes, IReadOnlyDictionary<string, NodeState> previousState)
    {
        foreach (var node in nodes)
        {
            if (!previousState.TryGetValue(node.FullPath, out var state))
            {
                continue;
            }

            if (state.IsExpanded && node.IsDirectory)
            {
                node.IsExpanded = true;

                if (node.Children is not null)
                {
                    ApplyNodeState(node.Children, previousState);
                }
            }

            if (state.IsSelected)
            {
                node.IsSelected = true;
            }
        }
    }

    /// <summary>詳細表示の列ヘッダークリック：同じ列なら昇順/降順を反転し、別の列なら昇順から並べ替える。</summary>
    private void SortByColumn(string column)
    {
        _sortAscending = _sortColumn == column ? !_sortAscending : true;
        _sortColumn = column;

        OnPropertyChanged(nameof(SortIndicatorName));
        OnPropertyChanged(nameof(SortIndicatorSize));
        OnPropertyChanged(nameof(SortIndicatorLastModified));
        OnPropertyChanged(nameof(SortIndicatorKind));

        var sorted = SortNodes(RootNodes).ToList();
        RootNodes.Clear();

        foreach (var node in sorted)
        {
            RootNodes.Add(node);
        }

        RebuildVisibleNodes();
    }

    /// <summary>フォルダを常に先頭にまとめた上で、現在の並び替え列・方向に従って並べ替える。</summary>
    private IEnumerable<FileSystemNodeViewModel> SortNodes(IEnumerable<FileSystemNodeViewModel> nodes)
    {
        var ordered = nodes.OrderByDescending(n => n.IsDirectory);

        return _sortColumn switch
        {
            "Size" => _sortAscending
                ? ordered.ThenBy(n => n.SizeBytes ?? 0)
                : ordered.ThenByDescending(n => n.SizeBytes ?? 0),
            "LastModified" => _sortAscending
                ? ordered.ThenBy(n => n.LastModified ?? DateTime.MinValue)
                : ordered.ThenByDescending(n => n.LastModified ?? DateTime.MinValue),
            "Kind" => _sortAscending
                ? ordered.ThenBy(n => n.KindDisplay, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(n => n.KindDisplay, StringComparer.CurrentCultureIgnoreCase),
            _ => _sortAscending
                ? ordered.ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(n => n.Name, StringComparer.CurrentCultureIgnoreCase),
        };
    }
}
