using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書41章：Win32のRegisterHotKeyを使い、他アプリ使用中でも呼び出せるホットキーを登録する。
/// 競合時（他アプリが同じ組み合わせを既に登録している場合）はRegisterHotKeyがfalseを返すため、
/// 呼び出し側で警告を表示できるようbool結果を返す。
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x3A20; // アプリ内で一意であればよい適当な値。

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private Action? _onPressed;
    private bool _registered;

    public bool Register(Window window, ModifierKeys modifiers, Key key, Action onPressed)
    {
        Unregister();

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(hwnd);
        if (_source is null)
        {
            return false;
        }

        _source.AddHook(WndProc);

        var fsModifiers = ToNativeModifiers(modifiers);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        _registered = RegisterHotKey(hwnd, HotkeyId, fsModifiers, vk);

        if (_registered)
        {
            _onPressed = onPressed;
        }
        else
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
        }

        _registered = false;
        _source = null;
        _onPressed = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _onPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= 0x0008;
        return result;
    }

    public void Dispose() => Unregister();
}
