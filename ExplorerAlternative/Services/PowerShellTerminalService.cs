using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// PowerShellプロセスをホストするターミナルサービス（仕様書9章・17章）。
///
/// VS Codeの統合ターミナルと同じ構造（ターミナル画面＋シェルプロセス）に寄せるため、
/// 標準出力を「行単位」ではなく生の文字ストリームとして読む。行単位で読むと改行で
/// 終わらないプロンプト（"PS C:\...&gt; "）が次の行が来るまで表示されず、結果として
/// 自前の入力欄を別に用意するしかなくなるため（旧実装がその構造だった）。
///
/// ConPTY（疑似コンソール）は実機で STATUS_DLL_INIT_FAILED となり断念したため、
/// 引き続き標準入出力リダイレクト方式を使う（経緯は仕様書17章）。
/// </summary>
public sealed class PowerShellTerminalService : IPowerShellTerminalService
{
    /// <summary>コンソールのアタッチはプロセス全体に影響するため、全ターミナルで直列化する。</summary>
    private static readonly object ConsoleAttachLock = new();

    private readonly string _shellExecutable;
    private readonly bool _loadProfile;
    private readonly TerminalOutputDecoder _outputDecoder = new();
    private readonly TerminalOutputDecoder _errorDecoder = new();
    private Process? _process;
    private CancellationTokenSource? _readCancellation;

    public PowerShellTerminalService(string shellExecutable, bool loadProfile = false)
    {
        _shellExecutable = shellExecutable;
        _loadProfile = loadProfile;
    }

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning => _process is { HasExited: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            // 注意: "-Command -" を付けるとPowerShellは標準入力をEOFまで読み切ってから
            // 一括実行するモードになり、対話的に1行ずつ実行されなくなる（ターミナルが
            // 「使えない」ように見える不具合の原因）。引数なしで起動し、通常の対話型
            // ホストとして標準入力を1行ずつ読み取らせる。
            var startInfo = new ProcessStartInfo
            {
                FileName = _shellExecutable,
                Arguments = _loadProfile ? "-NoLogo" : "-NoLogo -NoProfile",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // PowerShell 5.1はリダイレクトされた標準入力をOEMコードページとして読むため、
                // 書き込み側もそれに合わせる（日本語ファイル名のドラッグ&ドロップ対策）。
                StandardInputEncoding = TerminalOutputDecoder.ShellInputEncoding
            };

            // 外部CLIツール（git/npm/dotnet等）に色付き出力を促す。PowerShell 5.1自身は
            // リダイレクト時にANSIを出さないが、これらのツールの出力は素通りしてくるため
            // ANSIカラー表示（17章）が有効に働く。
            startInfo.Environment["TERM"] = "xterm-256color";
            startInfo.Environment["FORCE_COLOR"] = "1";
            startInfo.Environment["CLICOLOR_FORCE"] = "1";

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.Start();
            _process.StandardInput.AutoFlush = true;

            _readCancellation = new CancellationTokenSource();
            StartReading(_process.StandardOutput.BaseStream, _outputDecoder, _readCancellation.Token);
            StartReading(_process.StandardError.BaseStream, _errorDecoder, _readCancellation.Token);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            RaiseError($"PowerShellの起動に失敗しました。({ex.Message})");
        }
    }

    // 生バイトを読み、文字コードを自動判別して復号したうえで届いた分だけ通知する。
    private void StartReading(Stream stream, TerminalOutputDecoder decoder, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    var text = decoder.Decode(buffer, read);
                    if (text.Length > 0)
                    {
                        RaiseOutput(text);
                    }
                }

                var tail = decoder.Flush();
                if (tail.Length > 0)
                {
                    RaiseOutput(tail);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // プロセス終了・Dispose時の読み取り中断は正常系として扱う。
            }
        }, cancellationToken);
    }

    public void SendCommand(string command)
    {
        if (!IsRunning)
        {
            RaiseError("ターミナルが起動していません。");
            return;
        }

        try
        {
            _process!.StandardInput.WriteLine(command);
        }
        catch (IOException ex)
        {
            RaiseError($"コマンドの送信に失敗しました。({ex.Message})");
        }
    }

    public void SendRaw(string text)
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _process!.StandardInput.Write(text);
        }
        catch (IOException ex)
        {
            RaiseError($"入力の送信に失敗しました。({ex.Message})");
        }
    }

    /// <summary>
    /// Ctrl+Cによる実行中コマンドの中断（仕様書17章）。
    ///
    /// 本アプリはコンソールを持たないGUIプロセスだが、<c>CreateNoWindow</c>で起動した
    /// 子シェルは（ウィンドウが無いだけで）コンソールを持っている。そこで一時的に子の
    /// コンソールへアタッチし、そのコンソールに対してCtrl+Cイベントを発生させることで、
    /// 本来のCtrl+Cシグナルを送る。イベントはコンソールに紐づく全プロセス（アタッチ中の
    /// 自分自身を含む）へ配送されるため、送信前に自プロセスのCtrl+C処理を無効化して
    /// アプリ自体が終了しないようにする。
    ///
    /// アタッチはプロセス全体の状態を変えるため、複数ターミナルから同時に実行されないよう
    /// ロックで直列化し、UIスレッドを止めないようバックグラウンドで実行する。
    /// </summary>
    public void Interrupt()
    {
        if (!IsRunning)
        {
            return;
        }

        var processId = (uint)_process!.Id;

        Task.Run(() =>
        {
            lock (ConsoleAttachLock)
            {
                var attached = false;

                try
                {
                    // 送信するCtrl+Cで自分自身が終了しないよう、アタッチ前に無効化しておく。
                    NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, true);
                    NativeMethods.FreeConsole();

                    attached = NativeMethods.AttachConsole(processId);
                    if (!attached)
                    {
                        // コンソールへアタッチできない場合は、標準入力を読んでいる対話
                        // プロンプト向けにETX(0x03)を送るだけのフォールバックとする。
                        SendRaw("\u0003");
                        return;
                    }

                    NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CtrlCEvent, 0);

                    // イベントが配送されるまでの猶予（アタッチしたまま少し待つ）。
                    Thread.Sleep(200);
                }
                finally
                {
                    if (attached)
                    {
                        NativeMethods.FreeConsole();
                    }

                    NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, false);
                }
            }
        });
    }

    public void ChangeDirectory(string path)
    {
        SendCommand($"Set-Location -LiteralPath \"{path}\"");
    }

    public void Dispose()
    {
        try
        {
            _readCancellation?.Cancel();

            if (_process is { HasExited: false })
            {
                _process.StandardInput.WriteLine("exit");
                _process.WaitForExit(1000);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // 終了処理中の失敗はアプリ終了を妨げないよう無視する。
        }
        finally
        {
            _readCancellation?.Dispose();
            _process?.Dispose();
        }
    }

    private void RaiseOutput(string text)
    {
        Application.Current?.Dispatcher.Invoke(() => OutputReceived?.Invoke(this, text));
    }

    private void RaiseError(string message)
    {
        Application.Current?.Dispatcher.Invoke(() => ErrorOccurred?.Invoke(this, message));
    }

    private static class NativeMethods
    {
        internal const uint CtrlCEvent = 0;

        /// <summary>指定プロセスのコンソールへアタッチする。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AttachConsole(uint dwProcessId);

        /// <summary>現在アタッチしているコンソールから切り離す。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool FreeConsole();

        /// <summary>handlerRoutineにNULL・addにtrueを渡すと、自プロセスのCtrl+C処理を無効化する。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

        /// <summary>dwProcessGroupIdに0を渡すと、アタッチ中のコンソールの全プロセスへ送る。</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
    }
}
