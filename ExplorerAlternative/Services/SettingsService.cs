using System.IO;
using System.Text.Json;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 設定の一元管理（仕様書25章）。%AppData%\ExplorerAlternative\settings.json に保存する。
/// 不正な設定値（27章）が見つかった場合は例外を握りつぶし、既定値へフォールバックする。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ExplorerAlternative");
        Directory.CreateDirectory(directory);
        _settingsFilePath = Path.Combine(directory, "settings.json");
        Current = CreateDefault();
    }

    public AppSettings Current { get; private set; }

    public void Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            Current = CreateDefault();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefault();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 不正な設定値・読み込み失敗時は既定値へフォールバックし、アプリを継続させる。
            Current = CreateDefault();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, SerializerOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppOperationException("設定の保存に失敗しました。", ex);
        }
    }

    private static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            TextFileExtensions = new List<string>()
        };
    }
}
