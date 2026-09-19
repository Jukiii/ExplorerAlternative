namespace ExplorerAlternative.Services.Abstractions;

public interface IPowerShellTerminalService : IDisposable
{
    /// <summary>シェルの標準出力・標準エラーから届いた生テキスト（行単位ではなく、
    /// 届いたチャンクそのまま）。プロンプトのような改行で終わらない出力も欠落しない。
    /// ANSIエスケープシーケンスも除去せずそのまま渡す（仕様書17章）。</summary>
    event EventHandler<string>? OutputReceived;

    event EventHandler<string>? ErrorOccurred;

    bool IsRunning { get; }

    void Start();

    /// <summary>1行分のコマンドを実行する（末尾に改行を付けて送信する）。</summary>
    void SendCommand(string command);

    /// <summary>改行を付けずにそのまま書き込む（対話プロンプトへの応答等）。</summary>
    void SendRaw(string text);

    /// <summary>実行中のコマンドを中断する（Ctrl+C相当）。</summary>
    void Interrupt();

    void ChangeDirectory(string path);
}
