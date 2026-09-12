namespace ExplorerAlternative.Models;

/// <summary>
/// 外部ツール定義。仕様書22章：ツール名・実行ファイル・引数・対象を設定可能にする。
/// 引数の "{path}" はコマンド実行対象のフルパスに置換される。
/// </summary>
public sealed class ExternalToolDefinition
{
    public required string Name { get; set; }

    public required string ExecutablePath { get; set; }

    public string Arguments { get; set; } = "{path}";

    public ExternalToolTarget Target { get; set; } = ExternalToolTarget.Both;
}
