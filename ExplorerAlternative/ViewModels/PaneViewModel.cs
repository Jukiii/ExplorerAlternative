using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// メインペイン1枠分のビューモデル（仕様書4章・19章）。将来の分割ペイン対応のため、
/// タブは複数のPaneViewModelを保持できる構造にしている（Phase 1では1タブ1ペイン）。
/// </summary>
public sealed class PaneViewModel : ObservableObject
{
    private const string DropEffectFormat = "Preferred DropEffect";

    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly IVersionControlService _versionControlService;
    private readonly IExternalToolService _externalToolService;
    private readonly ISettingsService _settingsService;

    private string _currentPath;
    private ViewMode _currentViewMode;
    private bool _isAddressEditing;
    private string _addressEditText = string.Empty;
    private FileSystemNodeViewModel? _primarySelectedNode;
    private VersionControlInfo _vcsInfo = VersionControlInfo.None;
    private bool _isActive;

    public PaneViewModel(
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IVersionControlService versionControlService,
        IExternalToolService externalToolService,
        ISettingsService settingsService,
        string initialPath,
        ViewMode initialViewMode)
    {
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _versionControlService = versionControlService;
        _externalToolService = externalToolService;
        _settingsService = settingsService;
        _currentPath = initialPath;
        _currentViewMode = initialViewMode;

        NavigateToCommand = new RelayCommand(p => NavigateTo((string)p!));
        GoUpCommand = new RelayCommand(_ => GoUp());
        SetViewModeCommand = new RelayCommand(p => CurrentViewMode = (ViewMode)p!);
        ToggleExpandCommand = new RelayCommand(p => ((FileSystemNodeViewModel)p!).IsExpanded ^= true);
        OpenCommand = new RelayCommand(_ => OpenSelection(), _ => PrimarySelectedNode is not null);
        NewFolderCommand = new RelayCommand(_ => CreateNewFolder());
        RenameCommand = new RelayCommand(_ => RenameSelection(), _ => PrimarySelectedNode is not null);
        DeleteCommand = new RelayCommand(_ => DeleteSelection(), _ => SelectedNodes.Count > 0);
        CopyCommand = new RelayCommand(_ => CopySelectionToClipboard(isCut: false), _ => SelectedNodes.Count > 0);
        CutCommand = new RelayCommand(_ => CopySelectionToClipboard(isCut: true), _ => SelectedNodes.Count > 0);
        PasteCommand = new RelayCommand(_ => PasteFromClipboard());
        BeginAddressEditCommand = new RelayCommand(_ => BeginAddressEdit());
        CommitAddressEditCommand = new RelayCommand(_ => CommitAddressEdit());
        RunExternalToolCommand = new RelayCommand(p => RunExternalTool((ExternalToolDefinition)p!), _ => PrimarySelectedNode is not null);
        CancelAddressEditCommand = new RelayCommand(_ => IsAddressEditing = false);

        NavigateTo(_currentPath, addToHistory: false);
    }

    public event Action<string>? PathChanged;

    public ObservableCollection<FileSystemNodeViewModel> RootNodes { get; } = new();

    public ObservableCollection<FileSystemNodeViewModel> VisibleNodes { get; } = new();

    public ObservableCollection<BreadcrumbSegmentViewModel> BreadcrumbSegments { get; } = new();

    public ObservableCollection<FileSystemNodeViewModel> SelectedNodes { get; } = new();

    public RelayCommand NavigateToCommand { get; }

    public RelayCommand GoUpCommand { get; }

    public RelayCommand SetViewModeCommand { get; }

    public RelayCommand ToggleExpandCommand { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand NewFolderCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand CutCommand { get; }

    public RelayCommand PasteCommand { get; }

    public RelayCommand BeginAddressEditCommand { get; }

    public RelayCommand CommitAddressEditCommand { get; }

    public RelayCommand CancelAddressEditCommand { get; }

    public RelayCommand RunExternalToolCommand { get; }

    /// <summary>コンテキストメニューの「外部ツール」サブメニュー（仕様書22章）用。</summary>
    public IReadOnlyList<ExternalToolDefinition> ExternalTools => _settingsService.Current.ExternalTools;

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    /// <summary>「PC」相当（ドライブ一覧）にいるかどうか。</summary>
    public bool IsAtComputerRoot => string.IsNullOrEmpty(CurrentPath);

    public ViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set => SetProperty(ref _currentViewMode, value);
    }

    public bool IsAddressEditing
    {
        get => _isAddressEditing;
        private set => SetProperty(ref _isAddressEditing, value);
    }

    public string AddressEditText
    {
        get => _addressEditText;
        set => SetProperty(ref _addressEditText, value);
    }

    public FileSystemNodeViewModel? PrimarySelectedNode
    {
        get => _primarySelectedNode;
        private set => SetProperty(ref _primarySelectedNode, value);
    }

    public VersionControlInfo VcsInfo
    {
        get => _vcsInfo;
        private set => SetProperty(ref _vcsInfo, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void UpdateSelection(IEnumerable<FileSystemNodeViewModel> selected)
    {
        SelectedNodes.Clear();

        foreach (var node in selected)
        {
            SelectedNodes.Add(node);
        }

        PrimarySelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : SelectedNodes.LastOrDefault();
    }

    public void NavigateTo(string path) => NavigateTo(path, addToHistory: true);

    private void NavigateTo(string path, bool addToHistory)
    {
        try
        {
            var entries = IsPathComputerRoot(path)
                ? _fileSystemService.GetDrives()
                : _fileSystemService.GetChildren(path);

            CurrentPath = path;

            RootNodes.Clear();

            foreach (var entry in entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                RootNodes.Add(new FileSystemNodeViewModel(entry, 0, _fileSystemService, _dialogService, RebuildVisibleNodes));
            }

            RebuildVisibleNodes();
            RebuildBreadcrumb();

            VcsInfo = IsPathComputerRoot(path) ? VersionControlInfo.None : _versionControlService.Detect(path);

            PathChanged?.Invoke(path);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    public void RefreshCurrentFolder() => NavigateTo(CurrentPath);

    private static bool IsPathComputerRoot(string path) => string.IsNullOrEmpty(path);

    private void GoUp()
    {
        if (IsAtComputerRoot)
        {
            return;
        }

        var parent = _fileSystemService.GetParent(CurrentPath);
        NavigateTo(parent ?? string.Empty);
    }

    private void RebuildVisibleNodes()
    {
        VisibleNodes.Clear();
        AppendVisible(RootNodes);
    }

    private void AppendVisible(IEnumerable<FileSystemNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            VisibleNodes.Add(node);

            if (node.IsDirectory && node.IsExpanded && node.Children is not null)
            {
                AppendVisible(node.Children);
            }
        }
    }

    private void RebuildBreadcrumb()
    {
        BreadcrumbSegments.Clear();
        BreadcrumbSegments.Add(CreateSegment("PC", string.Empty));

        if (IsAtComputerRoot)
        {
            return;
        }

        var root = Path.GetPathRoot(CurrentPath) ?? string.Empty;
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        BreadcrumbSegments.Add(CreateSegment(root.TrimEnd('\\'), root));

        var relative = CurrentPath[root.Length..];
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var accumulated = root;

        foreach (var part in parts)
        {
            accumulated = Path.Combine(accumulated, part);
            BreadcrumbSegments.Add(CreateSegment(part, accumulated));
        }
    }

    private BreadcrumbSegmentViewModel CreateSegment(string name, string path)
    {
        return new BreadcrumbSegmentViewModel(name, path, p => NavigateTo(p), LoadDropdownChildren);
    }

    private IReadOnlyList<(string Name, string Path)> LoadDropdownChildren(string path)
    {
        try
        {
            var entries = IsPathComputerRoot(path) ? _fileSystemService.GetDrives() : _fileSystemService.GetChildren(path);
            return entries.Where(e => e.IsDirectory).Select(e => (e.Name, e.FullPath)).ToList();
        }
        catch (AppOperationException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private void BeginAddressEdit()
    {
        AddressEditText = CurrentPath;
        IsAddressEditing = true;
    }

    private void CommitAddressEdit()
    {
        IsAddressEditing = false;

        if (string.IsNullOrWhiteSpace(AddressEditText))
        {
            return;
        }

        if (!_fileSystemService.DirectoryExists(AddressEditText))
        {
            _dialogService.ShowError($"フォルダ「{AddressEditText}」が見つかりません。");
            return;
        }

        NavigateTo(AddressEditText);
    }

    private void OpenSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        if (target.IsDirectory)
        {
            NavigateTo(target.FullPath);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _dialogService.ShowError($"「{target.Name}」を開けませんでした。({ex.Message})");
        }
    }

    private void CreateNewFolder()
    {
        var name = _dialogService.PromptText("新しいフォルダ", "フォルダ名を入力してください。", "新しいフォルダ");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _fileSystemService.CreateDirectory(CurrentPath, name);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void RenameSelection()
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        var newName = _dialogService.PromptText("名前の変更", "新しい名前を入力してください。", target.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == target.Name)
        {
            return;
        }

        try
        {
            _fileSystemService.Rename(target.FullPath, newName);
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void DeleteSelection()
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        if (!_dialogService.Confirm($"選択した{SelectedNodes.Count}件をごみ箱へ移動しますか？"))
        {
            return;
        }

        try
        {
            _fileSystemService.Delete(SelectedNodes.Select(n => n.FullPath));
            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void CopySelectionToClipboard(bool isCut)
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        var fileList = new StringCollection();
        fileList.AddRange(SelectedNodes.Select(n => n.FullPath).ToArray());

        var dataObject = new DataObject();
        dataObject.SetFileDropList(fileList);

        var effect = isCut ? DragDropEffects.Move : DragDropEffects.Copy;
        dataObject.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes((int)effect)));

        Clipboard.SetDataObject(dataObject, true);
    }

    private void PasteFromClipboard()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return;
        }

        var files = Clipboard.GetFileDropList().Cast<string>().ToList();
        if (files.Count == 0)
        {
            return;
        }

        var isMove = false;
        var dataObject = Clipboard.GetDataObject();

        if (dataObject?.GetDataPresent(DropEffectFormat) == true &&
            dataObject.GetData(DropEffectFormat) is MemoryStream stream)
        {
            var buffer = new byte[4];
            stream.Position = 0;
            _ = stream.Read(buffer, 0, buffer.Length);
            var effect = (DragDropEffects)BitConverter.ToInt32(buffer, 0);
            isMove = effect.HasFlag(DragDropEffects.Move);
        }

        try
        {
            if (isMove)
            {
                _fileSystemService.Move(files, CurrentPath);
            }
            else
            {
                _fileSystemService.Copy(files, CurrentPath);
            }

            RefreshCurrentFolder();
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    public void RunExternalTool(ExternalToolDefinition tool)
    {
        var target = PrimarySelectedNode;
        if (target is null)
        {
            return;
        }

        try
        {
            _externalToolService.Run(tool, target.FullPath);
        }
        catch (AppOperationException ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    public void BulkRename(IReadOnlyList<FileSystemNodeViewModel> targets, string pattern)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var newName = pattern.Replace("{n}", (i + 1).ToString())
                .Replace("{name}", Path.GetFileNameWithoutExtension(targets[i].Name))
                .Replace("{ext}", Path.GetExtension(targets[i].Name).TrimStart('.'));

            try
            {
                _fileSystemService.Rename(targets[i].FullPath, newName);
            }
            catch (AppOperationException ex)
            {
                _dialogService.ShowError(ex.Message);
                break;
            }
        }

        RefreshCurrentFolder();
    }
}
