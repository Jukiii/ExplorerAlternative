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
/// PaneViewModelのうち、パンくずアドレスバーとアドレス編集（仕様書7章）。
/// </summary>
public sealed partial class PaneViewModel
{
    private void RebuildBreadcrumb()
    {
        BreadcrumbSegments.Clear();
        // 「PC」自身は親を持たないため、そのドロップダウンには自分の子（＝ドライブ一覧）を表示する。
        BreadcrumbSegments.Add(CreateSegment("PC", string.Empty, parentPath: null, remainder: string.Empty));

        if (IsAtComputerRoot)
        {
            return;
        }

        var root = Path.GetPathRoot(CurrentPath) ?? string.Empty;
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var driveRoot = root.TrimEnd('\\');
        BreadcrumbSegments.Add(CreateSegment(driveRoot, root, parentPath: string.Empty, remainder: ComputeRemainder(root)));

        var relative = CurrentPath[root.Length..];
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var accumulated = root;

        foreach (var part in parts)
        {
            var parentPath = accumulated;
            accumulated = Path.Combine(accumulated, part);
            BreadcrumbSegments.Add(CreateSegment(part, accumulated, parentPath, ComputeRemainder(accumulated)));
        }
    }

    // 仕様書11章：パンくずドロップダウンの右クリック「部分置換」用に、指定した階層より下の
    // パスを算出する（例：現在パスが C:\aa\2026\05 で階層が C:\aa の場合は "2026\05"）。
    private string ComputeRemainder(string segmentPath)
    {
        return CurrentPath.Length > segmentPath.Length
            ? CurrentPath[segmentPath.Length..].TrimStart(Path.DirectorySeparatorChar)
            : string.Empty;
    }

    private BreadcrumbSegmentViewModel CreateSegment(string name, string path, string? parentPath, string remainder)
    {
        return new BreadcrumbSegmentViewModel(name, path, parentPath, remainder, p => NavigateTo(p), NavigateToPartial, LoadDropdownChildren);
    }

    // 仕様書11章：右クリックでの部分パス置換。下層が存在しない場合は安全にそのフォルダへ移動する。
    private void NavigateToPartial(string basePath, string remainder)
    {
        if (!string.IsNullOrEmpty(remainder))
        {
            var candidate = Path.Combine(basePath, remainder);
            if (_fileSystemService.DirectoryExists(candidate))
            {
                NavigateTo(candidate);
                return;
            }
        }

        NavigateTo(basePath);
    }

    private IReadOnlyList<(string Name, string Path)> LoadDropdownChildren(string path)
    {
        try
        {
            var entries = IsPathComputerRoot(path) ? _fileSystemService.GetDrives() : _fileSystemService.GetChildren(path);
            return entries.Where(e => e.IsDirectory).Select(e => (e.Name, e.FullPath)).ToList();
        }
        catch (AppOperationException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private void BeginAddressEdit()
    {
        AddressEditText = CurrentPath;
        IsAddressEditing = true;
    }

    private void CommitAddressEdit()
    {
        IsAddressEditing = false;

        if (string.IsNullOrWhiteSpace(AddressEditText))
        {
            return;
        }

        if (!_fileSystemService.DirectoryExists(AddressEditText))
        {
            _dialogService.ShowError($"フォルダ「{AddressEditText}」が見つかりません。");
            return;
        }

        NavigateTo(AddressEditText);
    }
}
