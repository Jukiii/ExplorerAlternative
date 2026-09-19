using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書31章「ファイル操作履歴」：本アプリが実行したファイル操作を永続化して記録する。
/// IUndoServiceのメモリ上のスタック（アプリ終了で消える、Undo可能な操作のみ）とは異なり、
/// こちらは削除や失敗した操作も含めて記録し、検索・削除に対応する。
/// </summary>
public interface IFileOperationHistoryService
{
    /// <summary>新しい順。</summary>
    IReadOnlyList<FileOperationHistoryEntry> GetAll();

    void Record(FileOperationHistoryEntry entry);

    void Remove(FileOperationHistoryEntry entry);

    void Clear();
}
