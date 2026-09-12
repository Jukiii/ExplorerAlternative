using System.IO;
using System.Windows;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using Microsoft.Win32;

namespace ExplorerAlternative.Services;

/// <summary>
/// Application.Resourcesの先頭に置かれたテーマ用ResourceDictionaryを差し替えることで、
/// 実行中の全ウィンドウの外観（DynamicResourceで参照しているブラシ）を即座に切り替える。
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string LightThemeUri = "Themes/LightTheme.xaml";
    private const string DarkThemeUri = "Themes/DarkTheme.xaml";

    public void Apply(AppTheme theme)
    {
        var resolved = theme == AppTheme.System ? ResolveSystemTheme() : theme;
        var uri = resolved == AppTheme.Dark ? DarkThemeUri : LightThemeUri;

        var newDictionary = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };
        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

        // App.xaml自身が既定でLightThemeを読み込んでいるため、それも含めて既存のテーマ
        // 辞書をすべて除去してから追加する。MergedDictionariesは後ろにあるものほど優先される
        // ため、単純にInsert(0, ...)するとApp.xaml側の既定テーマに負けて反映されなかった。
        for (var i = mergedDictionaries.Count - 1; i >= 0; i--)
        {
            var source = mergedDictionaries[i].Source?.OriginalString;
            if (source is not null && (source.EndsWith(LightThemeUri) || source.EndsWith(DarkThemeUri)))
            {
                mergedDictionaries.RemoveAt(i);
            }
        }

        mergedDictionaries.Add(newDictionary);
    }

    /// <summary>Windowsの「設定 &gt; 個人用設定 &gt; 色」の明暗設定を読み取る。取得できない場合はLight。</summary>
    private static AppTheme ResolveSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0 ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // レジストリを読み取れない環境ではLightへフォールバックする（27章：エラーでクラッシュさせない）。
        }

        return AppTheme.Light;
    }
}
