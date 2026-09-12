using System.Collections.ObjectModel;
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
    private string _newExtension = string.Empty;

    public SettingsViewModel(ISettingsService settingsService, IDialogService dialogService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;

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
        SaveCommand = new RelayCommand(_ => Save());
    }

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
