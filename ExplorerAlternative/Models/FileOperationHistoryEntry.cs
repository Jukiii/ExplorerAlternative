using System.Text.Json.Serialization;

namespace ExplorerAlternative.Models;

/// <summary>仕様書31章「ファイル操作履歴」：本アプリが実行したファイル操作の記録1件。</summary>
public sealed class FileOperationHistoryEntry
{
    public DateTime Timestamp { get; set; }

    /// <summary>「移動」「コピー」「名前変更」「削除」「複製」「新規作成」「ショートカット作成」「一括リネーム」等。</summary>
    public required string Operation { get; set; }

    /// <summary>対象の表示名（1件なら名前、複数件なら「n件」）。</summary>
    public required string Target { get; set; }

    public string? OriginalLocation { get; set; }

    public string? Destination { get; set; }

    public bool Success { get; set; } = true;

    public string? ErrorMessage { get; set; }

    /// <summary>一覧表示用（成功/失敗）。settings.jsonには保存しない。</summary>
    [JsonIgnore]
    public string SuccessDisplay => Success ? "成功" : "失敗";
}
