using System.Collections.ObjectModel;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>ファイル操作キューダイアログ（仕様書26章）。</summary>
public sealed class FileOperationQueueViewModel : ObservableObject
{
    private readonly IFileOperationQueueService _queueService;

    public FileOperationQueueViewModel(IFileOperationQueueService queueService)
    {
        _queueService = queueService;

        PauseCommand = new RelayCommand(p => _queueService.Pause((FileOperationQueueItem)p!));
        ResumeCommand = new RelayCommand(p => _queueService.Resume((FileOperationQueueItem)p!));
        CancelCommand = new RelayCommand(p => _queueService.Cancel((FileOperationQueueItem)p!));
    }

    public ObservableCollection<FileOperationQueueItem> Items => _queueService.Items;

    public RelayCommand PauseCommand { get; }

    public RelayCommand ResumeCommand { get; }

    public RelayCommand CancelCommand { get; }
}
