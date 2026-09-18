namespace ExplorerAlternative.ViewModels;

public enum PreviewKind
{
    Text,
    Markdown,
    Folder,
    Image,
    /// <summary>仕様書13章「PDF」。フルレンダリングは行わず、ファイル情報表示＋既定アプリで開くボタンのみ提供する。</summary>
    Pdf,
    Unsupported
}
