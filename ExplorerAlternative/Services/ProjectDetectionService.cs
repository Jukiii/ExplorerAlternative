using System.IO;
using System.Linq;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書54章・56章：VersionControlServiceと同様に、現在パスから親方向へ探索する
/// （下位階層は探索しない）。
/// </summary>
public sealed class ProjectDetectionService : IProjectDetectionService
{
    // 仕様書54章の一覧に加え、dotnet CLIの新しいソリューション形式である*.slnxも対象とする
    // （*.slnの後継フォーマットで、本プロジェクト自身もこの形式を採用しているため）。
    private static readonly string[] MarkerPatterns =
    {
        "*.sln", "*.slnx", "*.csproj", "package.json", "pom.xml", "Cargo.toml", "go.mod", "CMakeLists.txt"
    };

    public ProjectInfo? Detect(string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var current = path;

        while (!string.IsNullOrEmpty(current))
        {
            var marker = TryFindMarkerIn(current);
            if (marker is not null)
            {
                var name = Path.GetFileName(current.TrimEnd('\\'));
                return new ProjectInfo
                {
                    Name = string.IsNullOrEmpty(name) ? current : name,
                    RootPath = current,
                    MarkerFile = marker
                };
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    public string? FindSolutionRoot(string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var current = path;

        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if (Directory.EnumerateFiles(current, "*.sln").Any() || Directory.EnumerateFiles(current, "*.slnx").Any())
                {
                    return current;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return null;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    private static string? TryFindMarkerIn(string directory)
    {
        try
        {
            foreach (var pattern in MarkerPatterns)
            {
                var match = Directory.EnumerateFiles(directory, pattern).FirstOrDefault();
                if (match is not null)
                {
                    return Path.GetFileName(match);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        return null;
    }
}
