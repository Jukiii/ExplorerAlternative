using System.Collections.ObjectModel;
using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// ナビゲーションペイン（仕様書6章・4章）：ドライブ・お気に入り・最近使った場所・
/// 最近開いたプロジェクト・タグ・ワークスペースの一覧と折りたたみ状態。
/// お気に入りは、クイックアクセス相当の標準フォルダ（Home/Desktop等）も統合した
/// 単一の一覧として扱う（初回起動時のみ標準フォルダを初期値として登録する）。
/// </summary>
public sealed class NavigationPaneViewModel : ObservableObject
{
    private const int MaxRecentPlaces = 5;
    private const int MaxRecentProjects = 10;
    private const int MaxFrequentPlaces = 5;

    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly Action<string> _navigate;
    private readonly Dictionary<string, int> _frequentPlaceVisitCounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _isCollapsed;

    public NavigationPaneViewModel(ISettingsService settingsService, IFileSystemService fileSystemService, IDialogService dialogService, Action<string> navigate)
    {
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _navigate = navigate;

        RefreshDrives();

        // 初回起動時（お気に入りが未設定）のみ、クイックアクセス相当の標準フォルダを
        // お気に入りの初期値として登録する（以降はユーザーが自由に追加・削除・並び替え可能）。
        if (_settingsService.Current.Favorites.Count == 0)
        {
            foreach (var (name, path) in GetDefaultFavoriteFolders())
            {
                _settingsService.Current.Favorites.Add(new FavoriteEntry { Name = name, Path = path });
            }

            _settingsService.Save();
        }

        foreach (var favorite in _settingsService.Current.Favorites)
        {
            Favorites.Add(favorite);
        }

        foreach (var path in _settingsService.Current.RecentPlaces.Take(MaxRecentPlaces))
        {
            RecentPlaces.Add(new FavoriteEntry { Name = DisplayNameFor(path), Path = path });
        }

        foreach (var visit in _settingsService.Current.FrequentPlaces)
        {
            _frequentPlaceVisitCounts[visit.Path] = visit.VisitCount;
        }

        RebuildFrequentPlacesDisplay();

        // 仕様書55章：起動時の読み込みでは、名前が重複するプロジェクトは先に現れた方
        // （＝保存順は最近使った順のため、より最近使った方）だけを残す（本修正より前の
        // 設定ファイルに含まれる重複の後片付けも兼ねる）。
        foreach (var project in _settingsService.Current.RecentProjects)
        {
            if (RecentProjects.Any(e => string.Equals(e.Name, project.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            RecentProjects.Add(project);
        }

        foreach (var tag in _settingsService.Current.TagDefinitions)
        {
            Tags.Add(tag);
        }

        foreach (var name in _settingsService.Current.Workspaces.Select(w => w.Name))
        {
            Workspaces.Add(name);
        }

        NavigateToEntryCommand = new RelayCommand(p => _navigate(((FavoriteEntry)p!).Path));
        RemoveFavoriteCommand = new RelayCommand(p => RemoveFavorite((FavoriteEntry)p!));
        RenameFavoriteCommand = new RelayCommand(p => RenameFavorite((FavoriteEntry)p!));
        MoveFavoriteUpCommand = new RelayCommand(p => MoveFavorite((FavoriteEntry)p!, -1));
        MoveFavoriteDownCommand = new RelayCommand(p => MoveFavorite((FavoriteEntry)p!, 1));
        RemoveRecentPlaceCommand = new RelayCommand(p => RemoveRecentPlace((FavoriteEntry)p!));
        ClearRecentPlacesCommand = new RelayCommand(_ => ClearRecentPlaces());
        RemoveFrequentPlaceCommand = new RelayCommand(p => RemoveFrequentPlace((FavoriteEntry)p!));
        ClearFrequentPlacesCommand = new RelayCommand(_ => ClearFrequentPlaces());
        RemoveRecentProjectCommand = new RelayCommand(p => RemoveRecentProject((FavoriteEntry)p!));
        ClearRecentProjectsCommand = new RelayCommand(_ => ClearRecentProjects());
        RemoveTagCommand = new RelayCommand(p => RemoveTag((TagDefinition)p!));
        EditTagCommand = new RelayCommand(p => EditTag((TagDefinition)p!));
        LoadWorkspaceCommand = new RelayCommand(p => WorkspaceOpenRequested?.Invoke((string)p!));
        ToggleCollapsedCommand = new RelayCommand(_ => IsCollapsed = !IsCollapsed);
    }

    /// <summary>左ペインの「ワークスペース」項目クリック時（仕様書43章）。実際の読み込みはMainWindowViewModelへ委譲する。</summary>
    public event Action<string>? WorkspaceOpenRequested;

    public ObservableCollection<FavoriteEntry> Drives { get; } = new();

    public ObservableCollection<FavoriteEntry> Favorites { get; } = new();

    public ObservableCollection<FavoriteEntry> RecentPlaces { get; } = new();

    /// <summary>よく使う場所（仕様書39章）：アクセス回数の多い順に上位を表示する。</summary>
    public ObservableCollection<FavoriteEntry> FrequentPlaces { get; } = new();

    /// <summary>仕様書55章「最近開いたプロジェクト」。</summary>
    public ObservableCollection<FavoriteEntry> RecentProjects { get; } = new();

    public ObservableCollection<TagDefinition> Tags { get; } = new();

    public ObservableCollection<string> Workspaces { get; } = new();

    public RelayCommand NavigateToEntryCommand { get; }

    public RelayCommand RemoveFavoriteCommand { get; }

    public RelayCommand RenameFavoriteCommand { get; }

    public RelayCommand MoveFavoriteUpCommand { get; }

    public RelayCommand MoveFavoriteDownCommand { get; }

    public RelayCommand RemoveRecentPlaceCommand { get; }

    public RelayCommand ClearRecentPlacesCommand { get; }

    public RelayCommand RemoveFrequentPlaceCommand { get; }

    public RelayCommand ClearFrequentPlacesCommand { get; }

    public RelayCommand RemoveRecentProjectCommand { get; }

    public RelayCommand ClearRecentProjectsCommand { get; }

    public RelayCommand RemoveTagCommand { get; }

    public RelayCommand EditTagCommand { get; }

    public RelayCommand LoadWorkspaceCommand { get; }

    public RelayCommand ToggleCollapsedCommand { get; }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetProperty(ref _isCollapsed, value);
    }

    private static IEnumerable<(string Name, string Path)> GetDefaultFavoriteFolders()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return ("Home", userProfile);
        yield return ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        yield return ("Downloads", Path.Combine(userProfile, "Downloads"));
        yield return ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        yield return ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        yield return ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        yield return ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
    }

    public void RefreshDrives()
    {
        Drives.Clear();

        foreach (var drive in _fileSystemService.GetDrives())
        {
            Drives.Add(new FavoriteEntry { Name = drive.Name, Path = drive.FullPath });
        }
    }

    public void AddFavorite(string name, string path)
    {
        if (Favorites.Any(f => f.Path == path))
        {
            return;
        }

        var entry = new FavoriteEntry { Name = name, Path = path };
        Favorites.Add(entry);
        _settingsService.Current.Favorites.Add(entry);
        _settingsService.Save();
    }

    private void RemoveFavorite(FavoriteEntry entry)
    {
        Favorites.Remove(entry);
        _settingsService.Current.Favorites.RemoveAll(f => f.Path == entry.Path);
        _settingsService.Save();
    }

    // 仕様書36章：お気に入りの名前変更。FavoriteEntryはINotifyPropertyChangedを実装しないため、
    // 表示更新のために要素そのものを差し替える（ObservableCollectionのReplace通知でUIが更新される）。
    private void RenameFavorite(FavoriteEntry entry)
    {
        var newName = _dialogService.PromptText("名前の変更", "新しい名前を入力してください。", entry.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name)
        {
            return;
        }

        var index = Favorites.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        var renamed = new FavoriteEntry { Name = newName, Path = entry.Path };
        Favorites[index] = renamed;

        var settingsList = _settingsService.Current.Favorites;
        var settingsIndex = settingsList.FindIndex(f => f.Path == entry.Path);
        if (settingsIndex >= 0)
        {
            settingsList[settingsIndex] = renamed;
        }

        _settingsService.Save();
    }

    // 仕様書4章：▲▼ボタンによる並び替え。
    private void MoveFavorite(FavoriteEntry entry, int offset)
    {
        var index = Favorites.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        MoveFavoriteToIndex(entry, index + offset);
    }

    /// <summary>
    /// お気に入りを任意の位置へ移動する。▲▼ボタン（隣接移動）に加え、
    /// お気に入り欄内でのドラッグ&ドロップによる並び替えからも呼び出す。
    /// </summary>
    public void MoveFavoriteToIndex(FavoriteEntry entry, int newIndex)
    {
        var index = Favorites.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Favorites.Count - 1);
        if (newIndex == index)
        {
            return;
        }

        Favorites.Move(index, newIndex);

        var settingsList = _settingsService.Current.Favorites;
        var settingsIndex = settingsList.FindIndex(f => f.Path == entry.Path);

        if (settingsIndex >= 0)
        {
            var moved = settingsList[settingsIndex];
            settingsList.RemoveAt(settingsIndex);
            settingsList.Insert(Math.Clamp(newIndex, 0, settingsList.Count), moved);
        }

        _settingsService.Save();
    }

    // 仕様書50章：最近使った場所。ナビゲーション（フォルダ移動）のたびに呼び出される。
    public void RecordRecentPlace(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var existing = RecentPlaces.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentPlaces.Remove(existing);
        }

        RecentPlaces.Insert(0, new FavoriteEntry { Name = DisplayNameFor(path), Path = path });

        while (RecentPlaces.Count > MaxRecentPlaces)
        {
            RecentPlaces.RemoveAt(RecentPlaces.Count - 1);
        }

        _settingsService.Current.RecentPlaces = RecentPlaces.Select(e => e.Path).ToList();
        _settingsService.Save();
    }

    private void RemoveRecentPlace(FavoriteEntry entry)
    {
        RecentPlaces.Remove(entry);
        _settingsService.Current.RecentPlaces.RemoveAll(p => p == entry.Path);
        _settingsService.Save();
    }

    private void ClearRecentPlaces()
    {
        RecentPlaces.Clear();
        _settingsService.Current.RecentPlaces.Clear();
        _settingsService.Save();
    }

    // 仕様書39章「よく使う場所」：ナビゲーション（フォルダ移動）のたびに呼び出し、
    // フォルダごとのアクセス回数を積み上げる。「最近使った場所」がMRU（直近順）なのに対し、
    // こちらは頻度ベースで、たまに開くフォルダより日常的によく開くフォルダを上位に残す。
    public void RecordFrequentPlaceVisit(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _frequentPlaceVisitCounts[path] = _frequentPlaceVisitCounts.GetValueOrDefault(path) + 1;

        _settingsService.Current.FrequentPlaces = _frequentPlaceVisitCounts
            .Select(kv => new FrequentPlaceVisit { Path = kv.Key, VisitCount = kv.Value })
            .ToList();
        _settingsService.Save();

        RebuildFrequentPlacesDisplay();
    }

    private void RebuildFrequentPlacesDisplay()
    {
        FrequentPlaces.Clear();

        foreach (var path in _frequentPlaceVisitCounts
                     .OrderByDescending(kv => kv.Value)
                     .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxFrequentPlaces)
                     .Select(kv => kv.Key))
        {
            FrequentPlaces.Add(new FavoriteEntry { Name = DisplayNameFor(path), Path = path });
        }
    }

    private void RemoveFrequentPlace(FavoriteEntry entry)
    {
        _frequentPlaceVisitCounts.Remove(entry.Path);
        _settingsService.Current.FrequentPlaces.RemoveAll(v => string.Equals(v.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        _settingsService.Save();
        RebuildFrequentPlacesDisplay();
    }

    private void ClearFrequentPlaces()
    {
        _frequentPlaceVisitCounts.Clear();
        _settingsService.Current.FrequentPlaces.Clear();
        _settingsService.Save();
        FrequentPlaces.Clear();
    }

    // 仕様書55章：最近開いたプロジェクト。プロジェクトルートへ実際に移動したときに呼び出される。
    // 同じ名前のプロジェクトが既にある場合は、上位階層（パスの階層が浅い方）を優先して
    // 1件のみ保持する。階層が同じかそれ以上に深い場合は、既存の項目を「最近使った」
    // 扱いで先頭へ移動するにとどめ、別名義での重複追加はしない。
    public void RecordRecentProject(string name, string rootPath)
    {
        var existingSamePath = RecentProjects.FirstOrDefault(e => string.Equals(e.Path, rootPath, StringComparison.OrdinalIgnoreCase));
        if (existingSamePath is not null)
        {
            RecentProjects.Remove(existingSamePath);
        }

        var existingSameName = RecentProjects.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingSameName is null)
        {
            RecentProjects.Insert(0, new FavoriteEntry { Name = name, Path = rootPath });
        }
        else
        {
            RecentProjects.Remove(existingSameName);
            var keepNew = PathDepth(rootPath) < PathDepth(existingSameName.Path);
            RecentProjects.Insert(0, keepNew ? new FavoriteEntry { Name = name, Path = rootPath } : existingSameName);
        }

        while (RecentProjects.Count > MaxRecentProjects)
        {
            RecentProjects.RemoveAt(RecentProjects.Count - 1);
        }

        _settingsService.Current.RecentProjects = RecentProjects.ToList();
        _settingsService.Save();
    }

    private static int PathDepth(string path) =>
        path.Count(c => c is '\\' or '/');

    private void RemoveRecentProject(FavoriteEntry entry)
    {
        RecentProjects.Remove(entry);
        _settingsService.Current.RecentProjects.RemoveAll(p => p.Path == entry.Path);
        _settingsService.Save();
    }

    private void ClearRecentProjects()
    {
        RecentProjects.Clear();
        _settingsService.Current.RecentProjects.Clear();
        _settingsService.Save();
    }

    public void AddTag(TagDefinition tag)
    {
        if (string.IsNullOrWhiteSpace(tag.Name) || Tags.Any(t => t.Name == tag.Name))
        {
            return;
        }

        Tags.Add(tag);
        _settingsService.Current.TagDefinitions.Add(tag);
        _settingsService.Save();
    }

    private void RemoveTag(TagDefinition tag)
    {
        Tags.Remove(tag);
        _settingsService.Current.TagDefinitions.RemoveAll(t => t.Name == tag.Name);

        foreach (var assignment in _settingsService.Current.TagAssignments)
        {
            assignment.Tags.Remove(tag.Name);
        }

        _settingsService.Current.TagAssignments.RemoveAll(a => a.Tags.Count == 0);
        _settingsService.Save();
    }

    /// <summary>仕様書5章：既存タグのアイコン・色・名前を編集する。</summary>
    private void EditTag(TagDefinition tag)
    {
        var editor = TagEditorViewModel.FromDefinition(tag);
        if (_dialogService.ShowTagEditor(editor) != true)
        {
            return;
        }

        var updated = editor.ToDefinition();
        if (string.IsNullOrWhiteSpace(updated.Name))
        {
            return;
        }

        if (updated.Name != tag.Name && Tags.Any(t => t.Name == updated.Name))
        {
            _dialogService.ShowError($"タグ「{updated.Name}」は既に存在します。");
            return;
        }

        var index = Tags.IndexOf(tag);
        if (index < 0)
        {
            return;
        }

        // 名前が変わった場合、既存の付与情報（TagAssignments）も新しい名前へ付け替える。
        if (updated.Name != tag.Name)
        {
            foreach (var assignment in _settingsService.Current.TagAssignments)
            {
                var i = assignment.Tags.IndexOf(tag.Name);
                if (i >= 0)
                {
                    assignment.Tags[i] = updated.Name;
                }
            }
        }

        Tags[index] = updated;
        var settingsIndex = _settingsService.Current.TagDefinitions.FindIndex(t => t.Name == tag.Name);
        if (settingsIndex >= 0)
        {
            _settingsService.Current.TagDefinitions[settingsIndex] = updated;
        }

        _settingsService.Save();
    }

    /// <summary>ワークスペースの保存・削除後に一覧を最新化する。</summary>
    public void RefreshWorkspaces(IReadOnlyList<string> names)
    {
        Workspaces.Clear();

        foreach (var name in names)
        {
            Workspaces.Add(name);
        }
    }

    private static string DisplayNameFor(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
