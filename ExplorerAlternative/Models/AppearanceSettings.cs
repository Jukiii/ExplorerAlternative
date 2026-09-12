namespace ExplorerAlternative.Models;

/// <summary>
/// 外観設定（仕様書63章）。
/// </summary>
public sealed class AppearanceSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
}
