using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ExplorerAlternative;

/// <summary>
/// 仕様書4章：横スクロールが存在する箇所すべてで、トラックパッドの横スワイプ
/// （<c>WM_MOUSEHWHEEL</c>）と、通常ホイール＋Shift（<c>WM_MOUSEWHEEL</c>、Windows/Office等
/// 一般的な「Shiftを押しながらホイール＝横スクロール」の慣習）の両方に対応する。
/// WPFのScrollViewerは既定でどちらもハンドリングしない（素の縦方向ホイールのみ対応）ため、
/// ウィンドウメッセージを直接フックしてカーソル位置直下のScrollViewerへ手動で反映する。
/// MainWindowだけにフックしていると、Log/フォルダ比較/SFTPブラウザ・Diff等の別ウィンドウ
/// （別HWND）では一切効かなくなるため、<see cref="RegisterForAllWindows"/>をアプリ起動時に
/// 一度呼び出し、以降に開かれる全てのWindow（MainWindow含む）へ自動的にフックする。
/// </summary>
public static class TrackpadScrollSupport
{
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHwheel = 0x020E;
    private const int MkShift = 0x0004;

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
        short delta;

        if (msg == WmMouseHwheel)
        {
            // トラックパッドの横スワイプは常に横スクロールとして扱う。
            delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
        }
        else if (msg == WmMouseWheel)
        {
            // 通常の（縦）ホイールはShiftが押されている場合のみ横スクロールとして扱う。
            // Shiftなしの場合はWPF既定の縦スクロール処理に任せるため何もしない。
            var keyState = unchecked((int)(wParam.ToInt64() & 0xFFFF));
            if ((keyState & MkShift) == 0)
            {
                return IntPtr.Zero;
            }

            delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
        }
        else
        {
            return IntPtr.Zero;
        }

        if (HwndSource.FromHwnd(hwnd) is not { RootVisual: Window window })
        {
            return IntPtr.Zero;
        }

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
