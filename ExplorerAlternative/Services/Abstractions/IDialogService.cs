using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// ユーザーへのダイアログ表示をViewModelから抽象化するためのサービス。
/// ViewにMessageBox等を直接書かず、ViewModelはこのインターフェースのみに依存する。
/// </summary>
public interface IDialogService
{
    void ShowError(string message);

    void ShowInfo(string message);

    bool Confirm(string message);

    string? PromptText(string title, string message, string defaultValue = "");

    string? SelectFromList(string title, string message, IReadOnlyList<string> items);

    /// <summary>Quick Look相当のプレビュー（仕様書11章）を非モーダルで表示する。既存の表示は差し替える。</summary>
    void ShowPreview(PreviewViewModel previewViewModel);

    void ClosePreview();

    void ShowCheatSheet();

    void ShowSettings(SettingsViewModel settingsViewModel);

    /// <summary>一括名前変更ダイアログ（仕様書21章）を表示する。OKで確定された場合はtrueを返す。</summary>
    bool ShowBulkRename(BulkRenameViewModel bulkRenameViewModel);

    /// <summary>ファイル保存ダイアログ（仕様書14.1章のPatch作成先選択などに使用）を表示する。キャンセル時はnull。</summary>
    string? ShowSaveFileDialog(string title, string filter, string defaultFileName);

    /// <summary>ファイル選択ダイアログ（仕様書14.2章のPatch適用元選択などに使用）を表示する。キャンセル時はnull。</summary>
    string? ShowOpenFileDialog(string title, string filter);

    /// <summary>SSH接続プロファイルの追加・編集ダイアログ（仕様書44章）を表示する。「保存」で確定された場合はtrueを返す。</summary>
    bool ShowSshConnection(SshConnectionViewModel sshConnectionViewModel);

    /// <summary>SSH接続の登録・管理ダイアログ（仕様書44章）を表示する。</summary>
    void ShowSshProfiles(SshProfilesViewModel sshProfilesViewModel);

    /// <summary>検索ダイアログ（仕様書12章）を非モーダルで表示する。</summary>
    void ShowSearch(SearchViewModel searchViewModel);

    /// <summary>ディスク解析ダイアログ（仕様書38章・58章・59章）を表示する。</summary>
    void ShowDiskAnalysis(DiskAnalysisViewModel diskAnalysisViewModel);

    /// <summary>コマンドパレット（仕様書46章）を表示する。</summary>
    void ShowCommandPalette(CommandPaletteViewModel commandPaletteViewModel);

    /// <summary>プロパティダイアログ（仕様書48章）を表示する。</summary>
    void ShowProperties(PropertiesViewModel propertiesViewModel);

    /// <summary>Diffウィンドウ（仕様書23章・25章）を非モーダルで表示する。</summary>
    void ShowDiff(DiffViewModel diffViewModel);

    /// <summary>Log / Show Commitウィンドウ（仕様書21章）を非モーダルで表示する。</summary>
    void ShowGitLog(GitLogViewModel gitLogViewModel);

    /// <summary>フォルダ比較・同期ウィンドウ（仕様書45章）を非モーダルで表示する。</summary>
    void ShowFolderCompare(FolderCompareViewModel folderCompareViewModel);

    /// <summary>SFTPリモートブラウザウィンドウ（仕様書44章）を非モーダルで表示する。</summary>
    void ShowSftpBrowser(SftpBrowserViewModel sftpBrowserViewModel);

    /// <summary>タグの追加・編集ダイアログ（仕様書5章：アイコン・色の選択）を表示する。「保存」で確定された場合はtrueを返す。</summary>
    bool ShowTagEditor(TagEditorViewModel tagEditorViewModel);
}
