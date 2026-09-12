using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// ワークスペース（仕様書17章）の永続化。実体は<see cref="ISettingsService"/>が保持する
/// 設定ファイル内のリストとして保存する。
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly ISettingsService _settingsService;

    public WorkspaceService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<string> GetWorkspaceNames()
    {
        return _settingsService.Current.Workspaces.Select(w => w.Name).ToList();
    }

    public WorkspaceState? GetWorkspace(string name)
    {
        return _settingsService.Current.Workspaces.FirstOrDefault(w => w.Name == name);
    }

    public void SaveWorkspace(WorkspaceState state)
    {
        var workspaces = _settingsService.Current.Workspaces;
        var existingIndex = workspaces.FindIndex(w => w.Name == state.Name);

        if (existingIndex >= 0)
        {
            workspaces[existingIndex] = state;
        }
        else
        {
            workspaces.Add(state);
        }

        _settingsService.Save();
    }

    public void DeleteWorkspace(string name)
    {
        _settingsService.Current.Workspaces.RemoveAll(w => w.Name == name);
        _settingsService.Save();
    }
}
