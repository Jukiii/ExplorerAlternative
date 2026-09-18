using System.Windows.Documents;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Rendering;

namespace ExplorerAlternative.ViewModels;

/// <summary>Patch適用前の内容確認ダイアログ（仕様書24章）の入力値。</summary>
public sealed class PatchPreviewViewModel : ObservableObject
{
    private PatchPreviewViewModel(string patchFilePath, string targetRootPath, FlowDocument document, int fileCount)
    {
        PatchFilePath = patchFilePath;
        TargetRootPath = targetRootPath;
        Document = document;
        FileCount = fileCount;
    }

    public static PatchPreviewViewModel Create(string patchFilePath, string targetRootPath, string patchText)
    {
        var document = PatchDiffRenderer.Render(patchText);
        var fileCount = PatchDiffRenderer.CountFiles(patchText);
        return new PatchPreviewViewModel(patchFilePath, targetRootPath, document, fileCount);
    }

    public string PatchFilePath { get; }

    public string TargetRootPath { get; }

    public FlowDocument Document { get; }

    public int FileCount { get; }

    public string Summary => FileCount > 0
        ? $"「{TargetRootPath}」へ、{FileCount}個のファイルへの変更を適用します。"
        : $"「{TargetRootPath}」へ適用します。";
}
