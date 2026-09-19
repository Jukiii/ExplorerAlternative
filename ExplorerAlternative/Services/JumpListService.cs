using System.Windows;
using System.Windows.Shell;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書39章：System.Windows.Shell.JumpListを使い、Windowsタスクバーの
/// ジャンプリストへ最近使ったフォルダ・お気に入り・ワークスペースを反映する。
/// JumpTask.CustomCategoryにより、カテゴリ名付きで表示できる。
/// ワークスペースは`--workspace "名前"`引数で直接起動できるようにする。
/// </summary>
public sealed class JumpListService : IJumpListService
{
    private const int MaxItemsPerCategory = 8;

    public void Rebuild(
        IEnumerable<(string Name, string Path)> recentPlaces,
        IEnumerable<(string Name, string Path)> frequentPlaces,
        IEnumerable<(string Name, string Path)> favorites,
        IEnumerable<string> workspaceNames)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        var jumpList = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };

        foreach (var (name, path) in recentPlaces.Take(MaxItemsPerCategory))
        {
            jumpList.JumpItems.Add(CreateTask(exePath, name, path, $"\"{path}\"", "最近使った場所"));
        }

        foreach (var (name, path) in frequentPlaces.Take(MaxItemsPerCategory))
        {
            jumpList.JumpItems.Add(CreateTask(exePath, name, path, $"\"{path}\"", "よく使う場所"));
        }

        foreach (var (name, path) in favorites.Take(MaxItemsPerCategory))
        {
            jumpList.JumpItems.Add(CreateTask(exePath, name, path, $"\"{path}\"", "お気に入り"));
        }

        foreach (var name in workspaceNames.Take(MaxItemsPerCategory))
        {
            jumpList.JumpItems.Add(CreateTask(exePath, name, "ワークスペースを開く", $"--workspace \"{name}\"", "ワークスペース"));
        }

        JumpList.SetJumpList(Application.Current, jumpList);
        jumpList.Apply();
    }

    private static JumpTask CreateTask(string exePath, string title, string description, string arguments, string category)
    {
        return new JumpTask
        {
            Title = title,
            Description = description,
            ApplicationPath = exePath,
            Arguments = arguments,
            IconResourcePath = exePath,
            CustomCategory = category
        };
    }
}
