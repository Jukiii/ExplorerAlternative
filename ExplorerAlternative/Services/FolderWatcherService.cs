using System.IO;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

public sealed class FolderWatcherService : IFolderWatcherService
{
    private FileSystemWatcher? _watcher;
    private string? _currentPath;

    public event Action? Changed;

    public void SetPath(string? path)
    {
        if (string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeWatcher();
        _currentPath = path;

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false
            };

            watcher.Created += OnRaised;
            watcher.Deleted += OnRaised;
            watcher.Renamed += OnRaised;
            watcher.Changed += OnRaised;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 仕様書27章：監視できない場合でもアプリをクラッシュさせない。
            // この場合、外部変更の自動反映は行われないが、手動更新（F5）は引き続き機能する。
        }
    }

    private void OnRaised(object sender, FileSystemEventArgs e) => Changed?.Invoke();

    private void OnError(object sender, ErrorEventArgs e) => Changed?.Invoke();

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnRaised;
        _watcher.Deleted -= OnRaised;
        _watcher.Renamed -= OnRaised;
        _watcher.Changed -= OnRaised;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        DisposeWatcher();
        _currentPath = null;
    }
}
