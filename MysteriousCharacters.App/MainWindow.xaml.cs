using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MysteriousCharacters.App.Models;
using MysteriousCharacters.App.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MysteriousCharacters.App;

public partial class MainWindow : System.Windows.Window
{
    private static readonly IReadOnlyList<Key> SupportedHotkeyKeys =
    [
        Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M,
        Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T, Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z,
        Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11,
        Key.F12
    ];

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
        HotkeyKeyComboBox.ItemsSource = SupportedHotkeyKeys;
        ApplySettingsToControls();

        Closing += MainWindow_OnClosing;
        StateChanged += MainWindow_OnStateChanged;
    }

    public void InitializeRuntime()
    {
        var windowHandle = new WindowInteropHelper(this).EnsureHandle();
        _hotkeyService = new GlobalHotkeyService(windowHandle);
        _hotkeyService.Pressed += HotkeyService_OnPressed;

        var registered = _hotkeyService.TryRegister(_settings.Hotkey);
        _trayIconService = new TrayIconService(
            () => _settings,
            SetEnabledFromTray,
            OpenSettings,
            ExitApplication);

        SetStatus($"已加载 {_transformer.RuleCount} 条汉字转换规则，无法可靠转换的字将保持原样。");
        if (!registered)
        {
            Notify("快捷键注册失败", $"快捷键 {_settings.Hotkey} 已被其他程序占用，请打开设置修改。");
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

    private async void HotkeyService_OnPressed(object? sender, EventArgs e)
    {
        await _coordinator.TryReplaceSelectionAsync();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var candidate = ReadSettingsFromControls();
        if (candidate is null)
        {
            return;
        }

        if (_hotkeyService is not null && !_hotkeyService.TryRegister(candidate.Hotkey))
        {
            WpfMessageBox.Show(
                this,
                $"快捷键 {candidate.Hotkey} 已被其他程序占用，请换一个组合。",
                "快捷键冲突",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings = candidate;
        SaveCurrentSettings();
        _trayIconService?.Refresh();
        SetStatus("设置已保存。智能混合策略和 100% 替换已启用。");
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
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
            SetStatus($"已导入 {importedCount} 条自定义规则，精细规则合计 {_transformer.RuleCount} 条。");
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
            SetStatus($"词典已重新加载，共 {_transformer.RuleCount} 条精细规则。");
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

    private AppSettings? ReadSettingsFromControls()
    {
        var modifiers = HotkeyModifiers.None;
        modifiers |= CtrlCheckBox.IsChecked == true ? HotkeyModifiers.Ctrl : HotkeyModifiers.None;
        modifiers |= AltCheckBox.IsChecked == true ? HotkeyModifiers.Alt : HotkeyModifiers.None;
        modifiers |= ShiftCheckBox.IsChecked == true ? HotkeyModifiers.Shift : HotkeyModifiers.None;
        modifiers |= WinCheckBox.IsChecked == true ? HotkeyModifiers.Win : HotkeyModifiers.None;

        if (modifiers == HotkeyModifiers.None)
        {
            WpfMessageBox.Show(
                this,
                "快捷键至少需要一个修饰键，例如 Ctrl 或 Alt。",
                "快捷键无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }

        var selectedKey = HotkeyKeyComboBox.SelectedItem is Key key ? key : Key.E;

        return new AppSettings
        {
            Enabled = EnabledCheckBox.IsChecked == true,
            Hotkey = new HotkeyGesture { Modifiers = modifiers, Key = selectedKey },
            RestoreClipboard = RestoreClipboardCheckBox.IsChecked == true,
            ShowNotifications = ShowNotificationsCheckBox.IsChecked == true,
            CopyTimeoutMilliseconds = _settings.CopyTimeoutMilliseconds,
            CopyRetryCount = _settings.CopyRetryCount,
            ModifierReleaseTimeoutMilliseconds = _settings.ModifierReleaseTimeoutMilliseconds,
            ClipboardRestoreDelayMilliseconds = _settings.ClipboardRestoreDelayMilliseconds,
            CustomDictionaryPath = _settings.CustomDictionaryPath,
            BlacklistedProcesses = BlacklistTextBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
        };
    }

    private void ApplySettingsToControls()
    {
        EnabledCheckBox.IsChecked = _settings.Enabled;
        CtrlCheckBox.IsChecked = _settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl);
        AltCheckBox.IsChecked = _settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        ShiftCheckBox.IsChecked = _settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        WinCheckBox.IsChecked = _settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Win);
        HotkeyKeyComboBox.SelectedItem = _settings.Hotkey.Key;
        RestoreClipboardCheckBox.IsChecked = _settings.RestoreClipboard;
        ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
        BlacklistTextBox.Text = string.Join(Environment.NewLine, _settings.BlacklistedProcesses);
        DictionaryPathTextBlock.Text = string.IsNullOrWhiteSpace(_settings.CustomDictionaryPath)
            ? "当前使用 3500 个常用字规则、内置汉字词典和偏旁家族库。无法可靠转换的非常用字会保持原样。"
            : $"当前自定义词典：{_settings.CustomDictionaryPath}";
    }

    private void SetEnabledFromTray(bool enabled)
    {
        Dispatcher.Invoke(() =>
        {
            _settings.Enabled = enabled;
            SaveCurrentSettings();
            ApplySettingsToControls();
            SetStatus(enabled ? "快捷键转换已开启。" : "快捷键转换已暂停。");
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

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }
}
