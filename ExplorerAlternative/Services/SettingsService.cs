using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 設定の一元管理（仕様書25章）。%AppData%\ExplorerAlternative\settings.json に保存する。
/// 不正な設定値（27章）が見つかった場合は例外を握りつぶし、既定値へフォールバックする。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    // 列挙型は数値ではなく名前で保存する。数値保存だと将来enumの項目を増減・並び替えた際に
    // 既存のsettings.jsonの値がずれて誤った列挙値を読み込んでしまう（実際にViewMode.Listを
    // 廃止した際に発生した）。
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
            Current = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? CreateDefault();
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
