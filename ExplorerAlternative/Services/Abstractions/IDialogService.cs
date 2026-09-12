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
}
