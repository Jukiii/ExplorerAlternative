using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// 仕様書40章：System.Windows.Forms.NotifyIconを使ってタスクトレイ常駐を実現する。
/// WPFアプリからの利用は一般的なパターンであり、専用のメッセージループは不要
/// （WPFのDispatcherが同じスレッドでWin32メッセージを汲み上げるため）。
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private NotifyIcon? _notifyIcon;

    public bool IsVisible => _notifyIcon?.Visible == true;

    public void Show(Window window, IReadOnlyList<TrayMenuItem> menuItems, Action onShowRequested, Action onSettingsRequested, Action onExitRequested)
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Explorer Alternative") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        BuildMenu(menu.Items, menuItems);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Explorerを表示", null, (_, _) => onShowRequested());
        menu.Items.Add("設定", null, (_, _) => onSettingsRequested());
        menu.Items.Add("終了", null, (_, _) => onExitRequested());

        _notifyIcon = new NotifyIcon
        {
            Icon = TryGetAppIcon(),
            Visible = true,
            Text = "Explorer Alternative",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => onShowRequested();
    }

    public void Hide()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private static void BuildMenu(ToolStripItemCollection items, IReadOnlyList<TrayMenuItem> menuItems)
    {
        foreach (var item in menuItems)
        {
            if (item.IsSeparator)
            {
                items.Add(new ToolStripSeparator());
                continue;
            }

            if (item.Children is { Count: > 0 } children)
            {
                var sub = new ToolStripMenuItem(item.Text);
                BuildMenu(sub.DropDownItems, children);
                items.Add(sub);
            }
            else
            {
                var leaf = new ToolStripMenuItem(item.Text, null, (_, _) => item.Execute?.Invoke())
                {
                    Enabled = item.Execute is not null
                };
                items.Add(leaf);
            }
        }
    }

    private static Icon? TryGetAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? null : Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.FileNotFoundException)
        {
            return null;
        }
    }

    public void Dispose() => Hide();
}
