using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>仕様書21章「Log」「Show Commit」。コミット履歴の一覧と、選択したコミットの
/// 変更内容（diff形式）を表示する。</summary>
public sealed class GitLogViewModel : ObservableObject
{
    private const int MaxCommitCount = 200;

    private readonly IVersionControlService _versionControlService;
    private readonly VersionControlInfo _vcsInfo;
    private CommitLogEntry? _selectedCommit;
    private string _diffText = string.Empty;

    public GitLogViewModel(IVersionControlService versionControlService, VersionControlInfo vcsInfo, string title, CommitLogEntry? initialSelection = null)
    {
        _versionControlService = versionControlService;
        _vcsInfo = vcsInfo;
        Title = title;
        Commits = versionControlService.GetCommitLog(vcsInfo, MaxCommitCount);

        var preselected = initialSelection is null
            ? null
            : Commits.FirstOrDefault(c => c.Revision == initialSelection.Revision);
        SelectedCommit = preselected ?? (Commits.Count > 0 ? Commits[0] : null);
    }

    public string Title { get; }

    public IReadOnlyList<CommitLogEntry> Commits { get; }

    public CommitLogEntry? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
            {
                DiffText = value is null
                    ? string.Empty
                    : _versionControlService.GetCommitDiff(_vcsInfo, value.Revision);
            }
        }
    }

    public string DiffText
    {
        get => _diffText;
        private set => SetProperty(ref _diffText, value);
    }
}
