using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

public interface IWorkspaceService
{
    IReadOnlyList<string> GetWorkspaceNames();

    WorkspaceState? GetWorkspace(string name);

    void SaveWorkspace(WorkspaceState state);

    void DeleteWorkspace(string name);
}
