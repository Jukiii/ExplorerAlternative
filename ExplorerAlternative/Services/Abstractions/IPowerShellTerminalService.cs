namespace ExplorerAlternative.Services.Abstractions;

public interface IPowerShellTerminalService : IDisposable
{
    event EventHandler<string>? OutputReceived;

    event EventHandler<string>? ErrorOccurred;

    bool IsRunning { get; }

    void Start();

    void SendCommand(string command);

    void ChangeDirectory(string path);
}
