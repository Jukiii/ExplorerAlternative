namespace ExplorerAlternative.Models;

/// <summary>
/// 外観設定（仕様書63章）。
/// </summary>
public sealed class AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>仕様書9章「アクティブペインの背景強調度」。0.0〜0.4程度を想定。</summary>
    public double ActivePaneHighlightOpacity { get; set; } = 0.12;
}
