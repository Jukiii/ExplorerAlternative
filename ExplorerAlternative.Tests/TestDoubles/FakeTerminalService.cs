using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Tests.TestDoubles;

/// <summary>
/// 実際のシェルを起動せずに、ターミナルのViewModel・画面をテストするための偽のシェルサービス。
/// 受け取ったコマンドを記録し、出力・エラーはテストから任意に流し込める。
/// </summary>
internal sealed class FakeTerminalService : IPowerShellTerminalService
{
    public event EventHandler<string>? OutputReceived;

    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning { get; set; }

    public int StartCount { get; private set; }

    public int InterruptCount { get; private set; }

    public List<string> Commands { get; } = new();

    public List<string> DirectoryChanges { get; } = new();

    public void Start()
    {
        StartCount++;
        IsRunning = true;
    }

    public void SendCommand(string command) => Commands.Add(command);

    public void SendRaw(string text)
    {
    }

    public void Interrupt() => InterruptCount++;

    public void ChangeDirectory(string path) => DirectoryChanges.Add(path);

    public void Dispose()
    {
    }

    public void RaiseOutput(string text) => OutputReceived?.Invoke(this, text);

    public void RaiseError(string message) => ErrorOccurred?.Invoke(this, message);
}
