using System.Windows;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// アプリの外観テーマ（Light/Dark/System）の適用を担当する（仕様書2章・63章）。
/// </summary>
public interface IThemeService
{
    /// <summary>指定されたテーマを即座に適用する。Systemの場合はWindowsの現在設定を反映する。</summary>
    void Apply(AppTheme theme);

    /// <summary>現在解決済みのテーマに合わせて、指定ウィンドウのタイトルバー（非クライアント領域）を明暗切替する。</summary>
    void ApplyTitleBarToWindow(Window window);
}
