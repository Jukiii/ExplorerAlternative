using System.Windows;
using System.Windows.Input;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書41章「グローバルホットキー」：他アプリ使用中でも本アプリを呼び出せるようにする。
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <returns>登録に成功したか。他アプリと競合している場合はfalse。</returns>
    bool Register(Window window, ModifierKeys modifiers, Key key, Action onPressed);

    void Unregister();
}
