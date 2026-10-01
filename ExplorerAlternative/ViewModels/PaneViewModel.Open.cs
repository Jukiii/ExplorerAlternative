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
/// PaneViewModelのうち、開く操作（既定のアプリ・関連付け・新しいタブ・Explorer・ターミナル）（仕様書20・34章）。
/// </summary>
public sealed partial class PaneViewModel
{
    private void OpenSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        if (target.IsDirectory)
        {
            NavigateTo(target.FullPath);
            return;
        }

        // 仕様書34章：「常にこのアプリで開く」で関連付けたアプリがあれば、それで開く。
        if (TryOpenWithAssociation(target))
        {
            return;
        }

        OpenWithSystemDefault(target);
    }

    // 「既定のアプリで開く」：関連付けに関係なく、Windowsの既定のアプリで開く。
    private void OpenWithSystemDefaultSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null || target.IsDirectory)
        {
            return;
        }

        OpenWithSystemDefault(target);
    }

    /// <summary>
    /// Windowsの既定のアプリでファイルを開く処理。実際のプロセス起動を伴うため、単体テストでは
    /// 差し替えて、起動せずに呼び出しだけを確認する。
    /// </summary>
    internal Action<string> SystemOpenFile { get; set; } = StartWithShell;

    private static void StartWithShell(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });

    private void OpenWithSystemDefault(FileSystemNodeViewModel target)
    {
        try
        {
            SystemOpenFile(target.FullPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"「{target.Name}」を開けませんでした。({ex.Message})");
        }
    }

    // 仕様書34章：関連付けたアプリでファイルを開く。関連付けが無い場合はfalse（呼び出し側が既定のアプリで開く）。
    // 関連付けたアプリが見つからない（削除・移動された）場合は、その旨を伝えてfalseを返し、既定のアプリで開く。
    private bool TryOpenWithAssociation(FileSystemNodeViewModel target)
    {
        var association = AppAssociationResolver.Find(_settingsService.Current.AppAssociations, target.FullPath);
        if (association is null)
        {
            return false;
        }

        if (!File.Exists(association.ExecutablePath))
        {
            _dialogService.ShowInfo(
                $"「{association.Extension}」に関連付けられたアプリが見つからないため、既定のアプリで開きます。\n" +
                $"（{association.ExecutablePath}）\n関連付けは、設定の「関連付け」で変更・解除できます。");
            return false;
        }

        try
        {
            _externalToolService.Run(
                new ExternalToolDefinition { Name = Path.GetFileName(association.ExecutablePath), ExecutablePath = association.ExecutablePath },
                target.FullPath);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }

        return true;
    }

    // 仕様書34章「常にこのアプリで開く」：アプリを選んで関連付け、そのアプリで今すぐ開く。
    // Windowsのファイル関連付け（システム設定）は変更しない。このアプリの中だけで有効。
    private void OpenWithAlways()
    {
        var target = PrimarySelectedNode;
        if (target is null || target.IsDirectory)
        {
            return;
        }

        if (AppAssociationResolver.NormalizeExtension(Path.GetExtension(target.FullPath)) is null)
        {
            _dialogService.ShowInfo($"「{target.Name}」には拡張子がないため、関連付けできません。");
            return;
        }

        var exePath = _dialogService.ShowOpenFileDialog(
            "常に開くアプリを選択",
            "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*");
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        var extension = AppAssociationResolver.Set(_settingsService.Current.AppAssociations, target.FullPath, exePath);
        if (extension is null)
        {
            return;
        }

        try
        {
            _settingsService.Save();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }

        if (!TryOpenWithAssociation(target))
        {
            OpenWithSystemDefault(target);
        }
    }

    // 仕様書10章・37章：フォルダを新しいタブで開く。実際のタブ作成はMainWindowViewModelへ委譲する。
    private void OpenInNewTab()
    {
        var target = PrimarySelectedNode;
        if (target is null || !target.IsDirectory)
        {
            return;
        }

        OpenInNewTabRequested?.Invoke(target.FullPath);
    }

    // 仕様書34章「アプリで開く」：任意のEXEを選択し、選択ファイルを引数として起動する（今回だけ指定）。
    private void OpenWithBrowse()
    {
        var target = PrimarySelectedNode;
        if (target is null || target.IsDirectory)
        {
            return;
        }

        var exePath = _dialogService.ShowOpenFileDialog("アプリを選択", "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*");
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"\"{target.FullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"「{target.Name}」を開けませんでした。({ex.Message})");
        }
    }

    // 仕様書35章「Windows Explorerで開く」：フォルダはそのまま開き、ファイルは
    // 親フォルダをExplorerで開いて対象を選択状態にする。
    private void OpenInWindowsExplorer()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        try
        {
            var arguments = target.IsDirectory
                ? $"\"{target.FullPath}\""
                : $"/select,\"{target.FullPath}\"";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"Windows Explorerで開けませんでした。({ex.Message})");
        }
    }

    // 仕様書19章：選択中の項目（フォルダ）を「ここ」とする。ファイルが選択されている場合は
    // その親フォルダ、何も選択されていない場合は現在のフォルダを対象にする。
    private string GetHereDirectory()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return CurrentPath;
        }

        return target.IsDirectory ? target.FullPath : (Path.GetDirectoryName(target.FullPath) ?? CurrentPath);
    }

    // 仕様書19章「ここでPowerShellを開く」：外部のPowerShellウィンドウを起動する。
    private void OpenPowerShellHere()
    {
        var directory = GetHereDirectory();

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settingsService.Current.Terminal.ShellExecutable,
                WorkingDirectory = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"PowerShellを起動できませんでした。({ex.Message})");
        }
    }

    // 仕様書19章「ここでターミナルを開く」：統合ターミナル（9章）で対象フォルダへ移動する。
    // 実行は既存のRunTerminalCommandRequested経由でMainWindowViewModelへ委譲する。
    private void OpenTerminalHere()
    {
        var directory = GetHereDirectory();
        RunTerminalCommandRequested?.Invoke($"Set-Location -LiteralPath \"{directory}\"");
    }
}
