namespace ExplorerAlternative.Services.Abstractions;

/// <summary>
/// 仕様書35章「Windows Explorer連携」：フォルダの右クリックメニューから
/// 本アプリを起動できるようにする（連携ON/OFF）。
/// </summary>
public interface IExplorerIntegrationService
{
    bool IsEnabled { get; }

    void Enable();

    void Disable();
}
