using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ExplorerAlternative;

/// <summary>
/// 仕様書4章：トラックパッドの横スワイプ（<c>WM_MOUSEHWHEEL</c>）による水平スクロール対応。
/// WPFのScrollViewerは既定でこれをハンドリングしない（縦方向の<c>WM_MOUSEWHEEL</c>のみ対応）
/// ため、ウィンドウメッセージを直接フックしてカーソル位置直下のScrollViewerへ手動で反映する。
/// MainWindowだけにフックしていると、Log/フォルダ比較/SFTPブラウザ等の別ウィンドウ（別HWND）
/// では一切効かなくなるため、<see cref="RegisterForAllWindows"/>をアプリ起動時に一度呼び出し、
/// 以降に開かれる全てのWindow（MainWindow含む）へ自動的にフックする。
/// </summary>
public static class TrackpadScrollSupport
{
    private const int WmMouseHwheel = 0x020E;

    public static void RegisterForAllWindows()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not Window window || PresentationSource.FromVisual(window) is not HwndSource source)
                {
                    return;
                }

                // 同じWindowでLoadedが複数回発生しても二重フックしないようにする。
                source.RemoveHook(HandleMessage);
                source.AddHook(HandleMessage);
            }));
    }

    private static IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseHwheel)
        {
            return IntPtr.Zero;
        }

        if (HwndSource.FromHwnd(hwnd) is not { RootVisual: Window window })
        {
            return IntPtr.Zero;
        }

        var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
        var screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

        var point = window.PointFromScreen(new Point(screenX, screenY));
        var scrollViewer = FindScrollViewerAt(window, point);
        if (scrollViewer is null)
        {
            return IntPtr.Zero;
        }

        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta / 3.0);
        handled = true;
        return IntPtr.Zero;
    }

    // カーソル直下から祖先方向へScrollViewerを探す際、最初に見つかったものをそのまま
    // 使うと、内側のListBox/TextBoxが持つ内部ScrollViewer（横スクロール無効）に当たって
    // しまうことがあるため、横方向に実際にスクロール可能な最も内側のScrollViewerを優先し、
    // 見つからなければ最初に見つかったものにフォールバックする。
    private static ScrollViewer? FindScrollViewerAt(Visual root, Point point)
    {
        var hit = VisualTreeHelper.HitTest(root, point)?.VisualHit;
        ScrollViewer? fallback = null;
        while (hit is not null)
        {
            if (hit is ScrollViewer scrollViewer)
            {
                if (scrollViewer.ScrollableWidth > 0)
                {
                    return scrollViewer;
                }

                fallback ??= scrollViewer;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return fallback;
    }
}
