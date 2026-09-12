using System.Collections.ObjectModel;
using System.IO;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// ナビゲーションペイン（仕様書6章・4章）：クイックアクセス・ドライブ・お気に入り・
/// 最近使った場所・ピン留めファイル・タグ・ワークスペースの一覧と折りたたみ状態。
/// </summary>
public sealed class NavigationPaneViewModel : ObservableObject
{
    private const int MaxRecentPlaces = 10;

    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;
    private readonly Action<string> _navigate;
    private readonly Action<string> _openFile;
    private bool _isCollapsed;

    public NavigationPaneViewModel(ISettingsService settingsService, IFileSystemService fileSystemService, Action<string> navigate, Action<string> openFile)
    {
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
        _navigate = navigate;
        _openFile = openFile;

        foreach (var (name, path) in GetQuickAccessFolders())
        {
            QuickAccess.Add(new FavoriteEntry { Name = name, Path = path });
        }

        RefreshDrives();

        foreach (var favorite in _settingsService.Current.Favorites)
        {
            Favorites.Add(favorite);
        }

        foreach (var path in _settingsService.Current.RecentPlaces)
        {
            RecentPlaces.Add(new FavoriteEntry { Name = DisplayNameFor(path), Path = path });
        }

        foreach (var pinned in _settingsService.Current.PinnedFiles)
        {
            PinnedFiles.Add(pinned);
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
        OpenPinnedFileCommand = new RelayCommand(p => _openFile(((FavoriteEntry)p!).Path));
        RemoveFavoriteCommand = new RelayCommand(p => RemoveFavorite((FavoriteEntry)p!));
        MoveFavoriteUpCommand = new RelayCommand(p => MoveFavorite((FavoriteEntry)p!, -1));
        MoveFavoriteDownCommand = new RelayCommand(p => MoveFavorite((FavoriteEntry)p!, 1));
        RemoveRecentPlaceCommand = new RelayCommand(p => RemoveRecentPlace((FavoriteEntry)p!));
        ClearRecentPlacesCommand = new RelayCommand(_ => ClearRecentPlaces());
        RemovePinnedFileCommand = new RelayCommand(p => RemovePinnedFile((FavoriteEntry)p!));
        RemoveTagCommand = new RelayCommand(p => RemoveTag((TagDefinition)p!));
        LoadWorkspaceCommand = new RelayCommand(p => WorkspaceOpenRequested?.Invoke((string)p!));
        ToggleCollapsedCommand = new RelayCommand(_ => IsCollapsed = !IsCollapsed);
    }

    /// <summary>左ペインの「ワークスペース」項目クリック時（仕様書43章）。実際の読み込みはMainWindowViewModelへ委譲する。</summary>
    public event Action<string>? WorkspaceOpenRequested;

    public ObservableCollection<FavoriteEntry> QuickAccess { get; } = new();

    public ObservableCollection<FavoriteEntry> Drives { get; } = new();

    public ObservableCollection<FavoriteEntry> Favorites { get; } = new();

    public ObservableCollection<FavoriteEntry> RecentPlaces { get; } = new();

    public ObservableCollection<FavoriteEntry> PinnedFiles { get; } = new();

    public ObservableCollection<TagDefinition> Tags { get; } = new();

    public ObservableCollection<string> Workspaces { get; } = new();

    public RelayCommand NavigateToEntryCommand { get; }

    public RelayCommand OpenPinnedFileCommand { get; }

    public RelayCommand RemoveFavoriteCommand { get; }

    public RelayCommand MoveFavoriteUpCommand { get; }

    public RelayCommand MoveFavoriteDownCommand { get; }

    public RelayCommand RemoveRecentPlaceCommand { get; }

    public RelayCommand ClearRecentPlacesCommand { get; }

    public RelayCommand RemovePinnedFileCommand { get; }

    public RelayCommand RemoveTagCommand { get; }

    public RelayCommand LoadWorkspaceCommand { get; }

    public RelayCommand ToggleCollapsedCommand { get; }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetProperty(ref _isCollapsed, value);
    }

    private static IEnumerable<(string Name, string Path)> GetQuickAccessFolders()
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

    // 仕様書4章：お気に入りの並び替え。
    private void MoveFavorite(FavoriteEntry entry, int offset)
    {
        var index = Favorites.IndexOf(entry);
        var newIndex = index + offset;

        if (index < 0 || newIndex < 0 || newIndex >= Favorites.Count)
        {
            return;
        }

        Favorites.Move(index, newIndex);

        var settingsList = _settingsService.Current.Favorites;
        var settingsIndex = settingsList.FindIndex(f => f.Path == entry.Path);

        if (settingsIndex >= 0)
        {
            var settingsNewIndex = settingsIndex + offset;
            if (settingsNewIndex >= 0 && settingsNewIndex < settingsList.Count)
            {
                (settingsList[settingsIndex], settingsList[settingsNewIndex]) = (settingsList[settingsNewIndex], settingsList[settingsIndex]);
            }
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

    // 仕様書51章：ピン留めファイル。
    public void AddPinnedFile(string name, string path)
    {
        if (PinnedFiles.Any(f => f.Path == path))
        {
            return;
        }

        var entry = new FavoriteEntry { Name = name, Path = path };
        PinnedFiles.Add(entry);
        _settingsService.Current.PinnedFiles.Add(entry);
        _settingsService.Save();
    }

    private void RemovePinnedFile(FavoriteEntry entry)
    {
        PinnedFiles.Remove(entry);
        _settingsService.Current.PinnedFiles.RemoveAll(f => f.Path == entry.Path);
        _settingsService.Save();
    }

    public void AddTag(string name)
    {
        if (Tags.Any(t => t.Name == name))
        {
            return;
        }

        var tag = new TagDefinition { Name = name };
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
