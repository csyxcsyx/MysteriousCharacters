using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action<bool> _setEnabled;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _enabledMenuItem;
    private readonly Drawing.Icon? _ownedIcon;
    private DateTime _lastNotificationAt = DateTime.MinValue;
    private string? _lastNotificationText;

    public TrayIconService(
        Func<AppSettings> settingsProvider,
        Action<bool> setEnabled,
        Action openSettings,
        Action exit)
    {
        _settingsProvider = settingsProvider;
        _setEnabled = setEnabled;

        var menu = new Forms.ContextMenuStrip();
        _enabledMenuItem = new Forms.ToolStripMenuItem();
        _enabledMenuItem.Click += (_, _) => ToggleEnabled();
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add("打开设置", null, (_, _) => openSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => exit());

        _ownedIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _ownedIcon ?? Drawing.SystemIcons.Application,
            Text = "隐文匣",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => openSettings();

        Refresh();
    }

    public void Refresh()
    {
        var settings = _settingsProvider();
        _enabledMenuItem.Checked = settings.Enabled;
        _enabledMenuItem.Text = settings.Enabled ? "暂停转换" : "开启转换";
        _notifyIcon.Text = settings.Enabled ? "隐文匣 - 智能混合" : "隐文匣 - 已暂停";
    }

    public void ShowNotification(string title, string message)
    {
        var now = DateTime.UtcNow;
        if (message == _lastNotificationText && now - _lastNotificationAt < TimeSpan.FromSeconds(4))
        {
            return;
        }

        _lastNotificationText = message;
        _lastNotificationAt = now;
        _notifyIcon.ShowBalloonTip(2500, title, message, Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _ownedIcon?.Dispose();
    }

    private void ToggleEnabled()
    {
        _setEnabled(!_settingsProvider().Enabled);
        Refresh();
    }
}
