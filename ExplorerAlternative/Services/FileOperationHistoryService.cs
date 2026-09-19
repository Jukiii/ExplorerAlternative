using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>仕様書31章「ファイル操作履歴」。settings.jsonへ永続化する。</summary>
public sealed class FileOperationHistoryService : IFileOperationHistoryService
{
    private const int MaxHistory = 500;

    private readonly ISettingsService _settingsService;

    public FileOperationHistoryService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<FileOperationHistoryEntry> GetAll() =>
        _settingsService.Current.FileOperationHistory
            .OrderByDescending(e => e.Timestamp)
            .ToList();

    public void Record(FileOperationHistoryEntry entry)
    {
        var history = _settingsService.Current.FileOperationHistory;
        history.Add(entry);

        while (history.Count > MaxHistory)
        {
            history.RemoveAt(0);
        }

        _settingsService.Save();
    }

    public void Remove(FileOperationHistoryEntry entry)
    {
        _settingsService.Current.FileOperationHistory.Remove(entry);
        _settingsService.Save();
    }

    public void Clear()
    {
        _settingsService.Current.FileOperationHistory.Clear();
        _settingsService.Save();
    }
}
