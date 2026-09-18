using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using ExplorerAlternative.Rendering;

namespace ExplorerAlternative.Models;

/// <summary>
/// 外部ツール定義。仕様書22章・42章：ツール名・実行ファイル・引数・対象を設定可能にする。
/// 引数の "{path}" はコマンド実行対象のフルパスに置換される。
/// </summary>
public sealed class ExternalToolDefinition
{
    public required string Name { get; set; }

    public required string ExecutablePath { get; set; }

    public string Arguments { get; set; } = "{path}";

    public ExternalToolTarget Target { get; set; } = ExternalToolTarget.Both;

    /// <summary>仕様書22章・42章「アイコン」：実行ファイル自体に関連付けられたアイコンをそのまま
    /// 表示する（ユーザーによる個別のアイコンファイル指定・永続化は行わない）。settings.jsonには
    /// 保存しない。</summary>
    [JsonIgnore]
    public BitmapSource? IconImage => IconExtractor.TryExtract(ExecutablePath);
}
