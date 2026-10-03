using System.Collections.Specialized;
using System.IO;
using System.Windows;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// ファイル・フォルダのコピー/切り取りの、クリップボードとの受け渡しの形式（Windowsのエクスプローラーと同じ）。
/// ファイルの一覧（FileDrop）に、「切り取り（移動）かコピーか」を表す<c>Preferred DropEffect</c>を添える。
/// このため、エクスプローラーでコピー/切り取りしたものを、このアプリへ貼り付けたり、その逆もできる。
/// 実際のクリップボードに触れずに確かめられるよう、データの組み立て・読み取りだけを、ここに分けてある。
/// </summary>
public static class FileClipboardFormat
{
    public const string DropEffectFormat = "Preferred DropEffect";

    public static DataObject CreateDataObject(IReadOnlyList<string> paths, bool isCut)
    {
        var fileList = new StringCollection();
        fileList.AddRange(paths.ToArray());

        var dataObject = new DataObject();
        dataObject.SetFileDropList(fileList);

        var effect = isCut ? DragDropEffects.Move : DragDropEffects.Copy;
        dataObject.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes((int)effect)));
        return dataObject;
    }

    /// <summary>
    /// クリップボードのデータから、ファイルの一覧と、移動（切り取り）かどうかを読み取る。
    /// ファイルが無ければ<c>null</c>。「Preferred DropEffect」が無い・読めない場合は、コピーとして扱う。
    /// </summary>
    public static FileClipboardContent? Read(IDataObject? dataObject)
    {
        if (dataObject is null || !dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        if (dataObject.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return null;
        }

        var isMove = false;

        if (dataObject.GetDataPresent(DropEffectFormat) && dataObject.GetData(DropEffectFormat) is MemoryStream stream)
        {
            var buffer = new byte[4];
            stream.Position = 0;

            if (stream.Read(buffer, 0, buffer.Length) == buffer.Length)
            {
                isMove = ((DragDropEffects)BitConverter.ToInt32(buffer, 0)).HasFlag(DragDropEffects.Move);
            }
        }

        return new FileClipboardContent(files, isMove);
    }
}

/// <summary>Windowsのクリップボードを使う、実際の<see cref="IFileClipboard"/>。</summary>
public sealed class WindowsFileClipboard : IFileClipboard
{
    public void SetFiles(IReadOnlyList<string> paths, bool isCut) =>
        Clipboard.SetDataObject(FileClipboardFormat.CreateDataObject(paths, isCut), true);

    public FileClipboardContent? GetFiles() => FileClipboardFormat.Read(Clipboard.GetDataObject());
}
