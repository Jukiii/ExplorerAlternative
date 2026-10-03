namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// ファイル・フォルダのコピー/切り取りの、クリップボードとの受け渡し（仕様書20章）。
/// Windowsのクリップボードは、テストで利用者のクリップボードを書き換えてしまうため、差し替えられるようにしてある。
/// </summary>
public interface IFileClipboard
{
    /// <summary>ファイルの一覧を、クリップボードへ入れる（<paramref name="isCut"/>が真なら切り取り＝貼り付けで移動）。</summary>
    void SetFiles(IReadOnlyList<string> paths, bool isCut);

    /// <summary>クリップボードのファイルの一覧を読み取る。ファイルが無ければ<c>null</c>。</summary>
    FileClipboardContent? GetFiles();
}

/// <summary>クリップボードにあるファイルの一覧と、貼り付けで移動するか（切り取り）、コピーするか。</summary>
public sealed record FileClipboardContent(IReadOnlyList<string> Files, bool IsMove);
