using System.Drawing;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerAlternative.Rendering;

/// <summary>
/// 仕様書22章・42章「外部ツール／カスタム送る」のアイコン表示：実行ファイルに関連付けられた
/// アイコンをWindowsから取得する。ユーザーによるアイコンファイルの個別指定は行わず、
/// 実行ファイル自体のアイコンをそのまま使う（設定の複雑化・アイコンファイルの永続化を避けるため）。
/// </summary>
public static class IconExtractor
{
    public static BitmapSource? TryExtract(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
