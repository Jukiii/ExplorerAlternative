namespace ExplorerAlternative.Models;

/// <summary>
/// 仕様書40章「システムトレイ」・41章「グローバルホットキー」の設定。
/// </summary>
public sealed class WindowsIntegrationSettings
{
    /// <summary>常駐ON/OFF。ONの場合、ウィンドウを閉じるとトレイに格納される。</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>グローバルホットキーの有効/無効。</summary>
    public bool GlobalHotkeyEnabled { get; set; }

    /// <summary>
    /// グローバルホットキーの修飾キー（Control/Alt/Shift/Windowsの組み合わせ）。
    /// <see cref="System.Windows.Input.ModifierKeys"/>の文字列表現として保存する。
    /// </summary>
    public string HotkeyModifiers { get; set; } = "Control, Alt";

    /// <summary>
    /// グローバルホットキーのキー本体。<see cref="System.Windows.Input.Key"/>の文字列表現として保存する。
    /// </summary>
    public string HotkeyKey { get; set; } = "E";
}
