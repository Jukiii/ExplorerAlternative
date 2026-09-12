using System.Collections.ObjectModel;
using System.Windows;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// 設定画面（仕様書25章）。Phase 1ではテキスト拡張子（16章）と外部ツール（22章）の
/// 一覧編集を扱う。お気に入り・タグはナビゲーションペインから直接編集する。
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IThemeService _themeService;
    private string _newExtension = string.Empty;
    private AppTheme _selectedTheme;
    private double _activePaneHighlightOpacity;
    private DuplicateTabBehavior _duplicateTabBehavior;
    private bool _loadTerminalProfile;

    public SettingsViewModel(ISettingsService settingsService, IDialogService dialogService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _themeService = themeService;
        _selectedTheme = settingsService.Current.Appearance.Theme;
        _activePaneHighlightOpacity = settingsService.Current.Appearance.ActivePaneHighlightOpacity;
        _duplicateTabBehavior = settingsService.Current.Tabs.DuplicateBehavior;
        _loadTerminalProfile = settingsService.Current.Terminal.LoadProfile;

        foreach (var extension in settingsService.Current.TextFileExtensions)
        {
            TextFileExtensions.Add(extension);
        }

        foreach (var tool in settingsService.Current.ExternalTools)
        {
            ExternalTools.Add(tool);
        }

        AddExtensionCommand = new RelayCommand(_ => AddExtension(), _ => !string.IsNullOrWhiteSpace(NewExtension));
        RemoveExtensionCommand = new RelayCommand(p => TextFileExtensions.Remove((string)p!));
        AddExternalToolCommand = new RelayCommand(_ => AddExternalTool());
        RemoveExternalToolCommand = new RelayCommand(p => ExternalTools.Remove((ExternalToolDefinition)p!));
        SetThemeCommand = new RelayCommand(p => SelectedTheme = (AppTheme)p!);
        SetDuplicateTabBehaviorCommand = new RelayCommand(p => DuplicateTabBehavior = (DuplicateTabBehavior)p!);
        SaveCommand = new RelayCommand(_ => Save());
    }

    /// <summary>仕様書9章「アクティブペインの背景強調度」。変更と同時に即座に適用・保存する。</summary>
    public double ActivePaneHighlightOpacity
    {
        get => _activePaneHighlightOpacity;
        set
        {
            if (SetProperty(ref _activePaneHighlightOpacity, value))
            {
                Application.Current.Resources["ActivePaneHighlightOpacity"] = value;
                _settingsService.Current.Appearance.ActivePaneHighlightOpacity = value;
                _settingsService.Save();
            }
        }
    }

    public RelayCommand SetDuplicateTabBehaviorCommand { get; }

    /// <summary>仕様書17章「PowerShellプロファイル」。次回以降に開くターミナルタブから反映される。</summary>
    public bool LoadTerminalProfile
    {
        get => _loadTerminalProfile;
        set
        {
            if (SetProperty(ref _loadTerminalProfile, value))
            {
                _settingsService.Current.Terminal.LoadProfile = value;
                _settingsService.Save();
            }
        }
    }

    /// <summary>仕様書37章「スマートタブ」。変更と同時に即座に保存する。</summary>
    public DuplicateTabBehavior DuplicateTabBehavior
    {
        get => _duplicateTabBehavior;
        set
        {
            if (SetProperty(ref _duplicateTabBehavior, value))
            {
                _settingsService.Current.Tabs.DuplicateBehavior = value;
                _settingsService.Save();
            }
        }
    }

    /// <summary>仕様書63章「外観 &gt; Light/Dark/System」。選択と同時に即座に適用・保存する。</summary>
    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _themeService.Apply(value);
                _settingsService.Current.Appearance.Theme = value;
                _settingsService.Save();
            }
        }
    }

    public RelayCommand SetThemeCommand { get; }

    public ObservableCollection<string> TextFileExtensions { get; } = new();

    public ObservableCollection<ExternalToolDefinition> ExternalTools { get; } = new();

    public string NewExtension
    {
        get => _newExtension;
        set => SetProperty(ref _newExtension, value);
    }

    public RelayCommand AddExtensionCommand { get; }

    public RelayCommand RemoveExtensionCommand { get; }

    public RelayCommand AddExternalToolCommand { get; }

    public RelayCommand RemoveExternalToolCommand { get; }

    public RelayCommand SaveCommand { get; }

    private void AddExtension()
    {
        var extension = NewExtension.TrimStart('.').Trim();

        if (string.IsNullOrEmpty(extension) || TextFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        TextFileExtensions.Add(extension);
        NewExtension = string.Empty;
    }

    private void AddExternalTool()
    {
        var name = _dialogService.PromptText("外部ツールの追加", "ツール名を入力してください。");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var executable = _dialogService.PromptText("外部ツールの追加", "実行ファイルのパスを入力してください。");
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        var arguments = _dialogService.PromptText(
            "外部ツールの追加",
            "引数を入力してください（{path} は対象のフルパスに置換されます）。",
            "{path}") ?? "{path}";

        ExternalTools.Add(new ExternalToolDefinition { Name = name, ExecutablePath = executable, Arguments = arguments });
    }

    public void Save()
    {
        _settingsService.Current.TextFileExtensions = TextFileExtensions.ToList();
        _settingsService.Current.ExternalTools = ExternalTools.ToList();
        _settingsService.Save();
    }
}
