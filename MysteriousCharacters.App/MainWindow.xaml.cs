using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using MysteriousCharacters.App.Models;
using MysteriousCharacters.App.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MysteriousCharacters.App;

public partial class MainWindow : System.Windows.Window
{
    private readonly SettingsService _settingsService;
    private readonly DictionaryService _dictionaryService;
    private readonly TextTransformer _transformer;
    private readonly TextReplacementCoordinator _coordinator;
    private AppSettings _settings;
    private GlobalHotkeyService? _hotkeyService;
    private TrayIconService? _trayIconService;
    private bool _allowExit;

    public MainWindow(SettingsService settingsService, DictionaryService dictionaryService)
    {
        _settingsService = settingsService;
        _dictionaryService = dictionaryService;
        _settings = _settingsService.Load();
        _transformer = new TextTransformer(_dictionaryService.LoadRules(_settings.CustomDictionaryPath));
        _coordinator = new TextReplacementCoordinator(
            new ClipboardService(),
            _transformer,
            () => _settings,
            Notify);

        InitializeComponent();
        ApplySettingsToControls();

        Closing += MainWindow_OnClosing;
    }

    public void InitializeRuntime()
    {
        var windowHandle = new WindowInteropHelper(this).EnsureHandle();
        _hotkeyService = new GlobalHotkeyService(windowHandle);
        _hotkeyService.Pressed += HotkeyService_OnPressed;

        var registered = _hotkeyService.TryRegister();
        _trayIconService = new TrayIconService(
            () => _settings,
            SetEnabledFromTray,
            OpenSettings,
            ExitApplication);

        SetStatus($"READY · 已加载 {_transformer.RuleCount} 条固定映射，可尝试还原 {_transformer.DecodeRuleCount} 种密文字。");
        if (!registered)
        {
            Notify("快捷键注册失败", "固定快捷键已被其他程序占用，请关闭占用快捷键的软件后重新启动。");
            OpenSettings();
        }
    }

    public void OpenSettings()
    {
        Dispatcher.Invoke(() =>
        {
            ApplySettingsToControls();
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private async void HotkeyService_OnPressed(TransformDirection direction)
    {
        await _coordinator.TryReplaceSelectionAsync(direction);
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings = ReadSettingsFromControls();
        SaveCurrentSettings();
        _trayIconService?.Refresh();
        SetStatus("SAVED · 设置已保存。");
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void ImportDictionaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "导入隐文匣自定义词典",
            Filter = "JSON 词典 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var importedCount = _dictionaryService.ImportCustomDictionary(dialog.FileName);
            _settings.CustomDictionaryPath = _dictionaryService.InstalledCustomDictionaryPath;
            ReloadDictionary();
            SaveCurrentSettings();
            ApplySettingsToControls();
            SetStatus($"IMPORTED · 已导入 {importedCount} 条自定义规则，固定映射合计 {_transformer.RuleCount} 条。");
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(
                this,
                exception.Message,
                "词典导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ReloadDictionaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ReloadDictionary();
            SetStatus($"RELOADED · 词典已重新加载，共 {_transformer.RuleCount} 条固定映射。");
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(
                this,
                exception.Message,
                "词典加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenDataFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settingsService.DataDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _settingsService.DataDirectory,
            UseShellExecute = true
        });
    }

    private AppSettings ReadSettingsFromControls()
    {
        return new AppSettings
        {
            Enabled = EnabledCheckBox.IsChecked == true,
            RestoreClipboard = RestoreClipboardCheckBox.IsChecked == true,
            ShowNotifications = ShowNotificationsCheckBox.IsChecked == true,
            CopyTimeoutMilliseconds = _settings.CopyTimeoutMilliseconds,
            CopyRetryCount = _settings.CopyRetryCount,
            ModifierReleaseTimeoutMilliseconds = _settings.ModifierReleaseTimeoutMilliseconds,
            ClipboardRestoreDelayMilliseconds = _settings.ClipboardRestoreDelayMilliseconds,
            CustomDictionaryPath = _settings.CustomDictionaryPath
        };
    }

    private void ApplySettingsToControls()
    {
        EnabledCheckBox.IsChecked = _settings.Enabled;
        RestoreClipboardCheckBox.IsChecked = _settings.RestoreClipboard;
        ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
        DictionaryPathTextBlock.Text = string.IsNullOrWhiteSpace(_settings.CustomDictionaryPath)
            ? "BUILT-IN · 当前使用 3500 个一级常用字固定映射。无法可靠转换的非常用字会保持原样。"
            : $"当前自定义词典：{_settings.CustomDictionaryPath}";
    }

    private void SetEnabledFromTray(bool enabled)
    {
        Dispatcher.Invoke(() =>
        {
            _settings.Enabled = enabled;
            SaveCurrentSettings();
            ApplySettingsToControls();
            SetStatus(enabled ? "RUNNING · 快捷键转换与尝试还原已开启。" : "PAUSED · 快捷键响应已暂停。");
        });
    }

    private void ReloadDictionary()
    {
        _transformer.ReplaceRules(_dictionaryService.LoadRules(_settings.CustomDictionaryPath));
    }

    private void SaveCurrentSettings()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception exception)
        {
            SetStatus($"设置保存失败：{exception.Message}");
        }
    }

    private void Notify(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            SetStatus(message);
            if (_settings.ShowNotifications)
            {
                _trayIconService?.ShowNotification(title, message);
            }
        });
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _allowExit = true;
            _hotkeyService?.Dispose();
            _trayIconService?.Dispose();
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
