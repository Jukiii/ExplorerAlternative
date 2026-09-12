using System.Windows;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書40章「システムトレイ」：設定で常駐ON/OFFを切り替え、トレイメニューから
/// 主要な機能へアクセスできるようにする。
/// </summary>
public interface ITrayIconService : IDisposable
{
    bool IsVisible { get; }

    void Show(Window window, IReadOnlyList<TrayMenuItem> menuItems, Action onShowRequested, Action onSettingsRequested, Action onExitRequested);

    void Hide();
}
