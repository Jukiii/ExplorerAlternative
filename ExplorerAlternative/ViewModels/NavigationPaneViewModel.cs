using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// ナビゲーションペイン（仕様書6章）：お気に入り・タグの一覧と折りたたみ状態。
/// </summary>
public sealed class NavigationPaneViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly Action<string> _navigate;
    private bool _isCollapsed;

    public NavigationPaneViewModel(ISettingsService settingsService, Action<string> navigate)
    {
        _settingsService = settingsService;
        _navigate = navigate;

        foreach (var favorite in _settingsService.Current.Favorites)
        {
            Favorites.Add(favorite);
        }

        foreach (var tag in _settingsService.Current.TagDefinitions)
        {
            Tags.Add(tag);
        }

        NavigateToFavoriteCommand = new RelayCommand(p => _navigate(((FavoriteEntry)p!).Path));
        RemoveFavoriteCommand = new RelayCommand(p => RemoveFavorite((FavoriteEntry)p!));
        ToggleCollapsedCommand = new RelayCommand(_ => IsCollapsed = !IsCollapsed);
    }

    public ObservableCollection<FavoriteEntry> Favorites { get; } = new();

    public ObservableCollection<TagDefinition> Tags { get; } = new();

    public RelayCommand NavigateToFavoriteCommand { get; }

    public RelayCommand RemoveFavoriteCommand { get; }

    public RelayCommand ToggleCollapsedCommand { get; }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetProperty(ref _isCollapsed, value);
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
}
