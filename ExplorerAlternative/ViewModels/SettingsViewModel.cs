using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
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
    private readonly IExplorerIntegrationService _explorerIntegrationService;
    private readonly Action<bool> _setTrayEnabled;
    private readonly Func<bool, ModifierKeys, Key, bool> _setGlobalHotkey;
    private string _newExtension = string.Empty;
    private AppTheme _selectedTheme;
    private double _activePaneHighlightOpacity;
    private DuplicateTabBehavior _duplicateTabBehavior;
    private bool _loadTerminalProfile;
    private bool _explorerIntegrationEnabled;
    private bool _minimizeToTray;
    private bool _globalHotkeyEnabled;
    private bool _hotkeyCtrl;
    private bool _hotkeyAlt;
    private bool _hotkeyShift;
    private bool _hotkeyWin;
    private string _hotkeyKeyText;

    public SettingsViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IThemeService themeService,
        IExplorerIntegrationService explorerIntegrationService,
        Action<bool> setTrayEnabled,
        Func<bool, ModifierKeys, Key, bool> setGlobalHotkey)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _themeService = themeService;
        _explorerIntegrationService = explorerIntegrationService;
        _setTrayEnabled = setTrayEnabled;
        _setGlobalHotkey = setGlobalHotkey;
        _selectedTheme = settingsService.Current.Appearance.Theme;
        _activePaneHighlightOpacity = settingsService.Current.Appearance.ActivePaneHighlightOpacity;
        _duplicateTabBehavior = settingsService.Current.Tabs.DuplicateBehavior;
        _loadTerminalProfile = settingsService.Current.Terminal.LoadProfile;
        _explorerIntegrationEnabled = explorerIntegrationService.IsEnabled;

        var windowsIntegration = settingsService.Current.WindowsIntegration;
        _minimizeToTray = windowsIntegration.MinimizeToTray;
        _globalHotkeyEnabled = windowsIntegration.GlobalHotkeyEnabled;
        var savedModifiers = Enum.TryParse<ModifierKeys>(windowsIntegration.HotkeyModifiers, out var parsedModifiers)
            ? parsedModifiers
            : ModifierKeys.Control | ModifierKeys.Alt;
        _hotkeyCtrl = savedModifiers.HasFlag(ModifierKeys.Control);
        _hotkeyAlt = savedModifiers.HasFlag(ModifierKeys.Alt);
        _hotkeyShift = savedModifiers.HasFlag(ModifierKeys.Shift);
        _hotkeyWin = savedModifiers.HasFlag(ModifierKeys.Windows);
        _hotkeyKeyText = windowsIntegration.HotkeyKey;

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
        ApplyHotkeyCommand = new RelayCommand(_ => TryApplyHotkey());
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

    /// <summary>
    /// 仕様書35章「Windows Explorer連携」の連携ON/OFF。フォルダの右クリックメニューへの
    /// 登録・削除をレジストリ（HKEY_CURRENT_USER）に対して即座に行う。
    /// </summary>
    public bool ExplorerIntegrationEnabled
    {
        get => _explorerIntegrationEnabled;
        set
        {
            if (SetProperty(ref _explorerIntegrationEnabled, value))
            {
                if (value)
                {
                    _explorerIntegrationService.Enable();
                }
                else
                {
                    _explorerIntegrationService.Disable();
                }
            }
        }
    }

    /// <summary>仕様書40章「システムトレイ」の常駐ON/OFF。変更と同時に即座に反映・保存する。</summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetProperty(ref _minimizeToTray, value))
            {
                _settingsService.Current.WindowsIntegration.MinimizeToTray = value;
                _settingsService.Save();
                _setTrayEnabled(value);
            }
        }
    }

    /// <summary>仕様書41章「グローバルホットキー」の有効/無効。</summary>
    public bool GlobalHotkeyEnabled
    {
        get => _globalHotkeyEnabled;
        set
        {
            if (SetProperty(ref _globalHotkeyEnabled, value))
            {
                TryApplyHotkey();
            }
        }
    }

    public bool HotkeyCtrl
    {
        get => _hotkeyCtrl;
        set => SetProperty(ref _hotkeyCtrl, value);
    }

    public bool HotkeyAlt
    {
        get => _hotkeyAlt;
        set => SetProperty(ref _hotkeyAlt, value);
    }

    public bool HotkeyShift
    {
        get => _hotkeyShift;
        set => SetProperty(ref _hotkeyShift, value);
    }

    public bool HotkeyWin
    {
        get => _hotkeyWin;
        set => SetProperty(ref _hotkeyWin, value);
    }

    /// <summary>グローバルホットキーのキー本体（WPFの<see cref="Key"/>名。例："E"、"F12"）。</summary>
    public string HotkeyKeyText
    {
        get => _hotkeyKeyText;
        set => SetProperty(ref _hotkeyKeyText, value);
    }

    public RelayCommand ApplyHotkeyCommand { get; }

    // 仕様書41章：「競合時は警告」。RegisterHotKeyが失敗した場合はエラーを表示し、有効状態を元に戻す。
    private void TryApplyHotkey()
    {
        if (!Enum.TryParse<Key>(HotkeyKeyText.Trim(), ignoreCase: true, out var key))
        {
            if (GlobalHotkeyEnabled)
            {
                _dialogService.ShowError($"「{HotkeyKeyText}」は有効なキー名ではありません。");
                _globalHotkeyEnabled = false;
                OnPropertyChanged(nameof(GlobalHotkeyEnabled));
            }

            return;
        }

        var modifiers = ModifierKeys.None;
        if (HotkeyCtrl) modifiers |= ModifierKeys.Control;
        if (HotkeyAlt) modifiers |= ModifierKeys.Alt;
        if (HotkeyShift) modifiers |= ModifierKeys.Shift;
        if (HotkeyWin) modifiers |= ModifierKeys.Windows;

        var succeeded = _setGlobalHotkey(GlobalHotkeyEnabled, modifiers, key);

        _settingsService.Current.WindowsIntegration.GlobalHotkeyEnabled = GlobalHotkeyEnabled && succeeded;
        _settingsService.Current.WindowsIntegration.HotkeyModifiers = modifiers.ToString();
        _settingsService.Current.WindowsIntegration.HotkeyKey = key.ToString();
        _settingsService.Save();

        if (GlobalHotkeyEnabled && !succeeded)
        {
            _dialogService.ShowError("指定したホットキーは他のアプリと競合しているため登録できませんでした。別の組み合わせを指定してください。");
            _globalHotkeyEnabled = false;
            OnPropertyChanged(nameof(GlobalHotkeyEnabled));
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
