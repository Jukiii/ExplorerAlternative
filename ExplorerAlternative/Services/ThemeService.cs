using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;
using Microsoft.Win32;

namespace ExplorerAlternative.Services;

/// <summary>
/// Application.Resourcesの先頭に置かれたテーマ用ResourceDictionaryを差し替えることで、
/// 実行中の全ウィンドウの外観（DynamicResourceで参照しているブラシ）を即座に切り替える。
/// また、DWM APIでウィンドウのタイトルバー（WPFのBackgroundが及ばない非クライアント領域）
/// も明暗に追従させる。
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string LightThemeUri = "Themes/LightTheme.xaml";
    private const string DarkThemeUri = "Themes/DarkTheme.xaml";
    private const int DwmwaUseImmersiveDarkMode = 20;

    private bool _isDarkResolved;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public void Apply(AppTheme theme)
    {
        var resolved = theme == AppTheme.System ? ResolveSystemTheme() : theme;
        _isDarkResolved = resolved == AppTheme.Dark;
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

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBarToWindow(window);
        }
    }

    /// <summary>
    /// タイトルバー（非クライアント領域）の明暗をDWM経由で設定する。WPFのBackgroundは
    /// クライアント領域にしか及ばないため、これを行わないとダークテーマでもタイトルバー
    /// だけ白いままになる。DWMAPIが存在しない/失敗する環境では見た目上の問題に留まる
    /// ため、例外は握りつぶして継続する（27章）。
    /// </summary>
    public void ApplyTitleBarToWindow(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var value = _isDarkResolved ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));

            // 属性を反映させるには非クライアント領域の再描画が必要な場合があるため、
            // 実際のサイズ・位置・Z順は変えずにフレーム再計算だけを強制する。
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // 古いWindowsビルド等、DWM APIが利用できない環境では見た目上の差異に留める。
        }
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
