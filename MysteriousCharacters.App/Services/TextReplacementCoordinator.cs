using System.Diagnostics;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class TextReplacementCoordinator
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "1Password",
        "Bitwarden",
        "CredentialUIBroker",
        "KeePass",
        "KeePassXC",
        "LockApp",
        "LogonUI",
        "mstsc"
    };

    private readonly ClipboardService _clipboardService;
    private readonly TextTransformer _transformer;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action<string, string> _notify;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public TextReplacementCoordinator(
        ClipboardService clipboardService,
        TextTransformer transformer,
        Func<AppSettings> settingsProvider,
        Action<string, string> notify)
    {
        _clipboardService = clipboardService;
        _transformer = transformer;
        _settingsProvider = settingsProvider;
        _notify = notify;
    }

    public async Task TryReplaceSelectionAsync(TransformDirection direction)
    {
        if (!await _operationLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var settings = _settingsProvider();
            if (!settings.Enabled)
            {
                return;
            }

            var foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero || IsProtectedProcess(foregroundWindow))
            {
                return;
            }

            if (!await WaitForModifierKeysReleasedAsync(settings.ModifierReleaseTimeoutMilliseconds))
            {
                _notify("快捷键仍处于按下状态", "请松开快捷键后重试。");
                return;
            }

            if (foregroundWindow != NativeMethods.GetForegroundWindow())
            {
                return;
            }

            if (!_clipboardService.TryCapture(out var snapshot) || snapshot is null)
            {
                _notify("剪贴板暂时不可用", "无法保存当前剪贴板内容，本次转换已跳过。");
                return;
            }

            var selectedText = await TryCopySelectionAsync(settings, foregroundWindow);
            if (selectedText is null)
            {
                Restore(snapshot);
                _notify(
                    "没有读取到选中文本",
                    "请确认已选中文字。若目标软件以管理员身份运行，请以相同权限启动隐文匣。");
                return;
            }

            var transformedText = _transformer.Transform(selectedText, direction);
            if (string.Equals(selectedText, transformedText, StringComparison.Ordinal))
            {
                Restore(snapshot);
                _notify(
                    direction == TransformDirection.Encode ? "没有可转换的汉字" : "没有可还原的汉字",
                    direction == TransformDirection.Encode
                        ? "当前选区不包含可处理的汉字或自定义规则。"
                        : "当前选区没有匹配到可尝试还原的文字。");
                return;
            }

            if (!_clipboardService.TrySetText(transformedText) ||
                !NativeMethods.SendShortcut(NativeMethods.VkV))
            {
                Restore(snapshot);
                _notify("粘贴失败", "目标窗口可能受权限或安全策略保护，本次转换已跳过。");
                return;
            }

            if (settings.RestoreClipboard)
            {
                await Task.Delay(settings.ClipboardRestoreDelayMilliseconds);
                _clipboardService.TryRestore(snapshot);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<string?> TryCopySelectionAsync(AppSettings settings, IntPtr foregroundWindow)
    {
        var fallbackTimeout = Math.Min(settings.CopyTimeoutMilliseconds, 700);

        for (var attempt = 0; attempt < settings.CopyRetryCount; attempt++)
        {
            var copiedText = await TryCopyWithAsync(
                () => NativeMethods.SendShortcut(NativeMethods.VkC),
                settings.CopyTimeoutMilliseconds);
            if (copiedText is not null)
            {
                return copiedText;
            }

            copiedText = await TryCopyWithAsync(
                () => NativeMethods.TryCopyFocusedControl(foregroundWindow),
                fallbackTimeout);
            if (copiedText is not null)
            {
                return copiedText;
            }

            copiedText = await TryCopyWithAsync(
                () => NativeMethods.SendShortcut(NativeMethods.VkInsert),
                fallbackTimeout);
            if (copiedText is not null)
            {
                return copiedText;
            }

            await Task.Delay(80);
        }

        return null;
    }

    private async Task<string?> TryCopyWithAsync(Func<bool> copy, int timeoutMilliseconds)
    {
        if (!_clipboardService.TryClear())
        {
            return null;
        }

        await Task.Delay(45);
        return copy() ? await WaitForCopiedTextAsync(timeoutMilliseconds) : null;
    }

    private static async Task<bool> WaitForModifierKeysReleasedAsync(int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (NativeMethods.AreModifierKeysReleased())
            {
                await Task.Delay(35);
                return true;
            }

            await Task.Delay(18);
        }

        return false;
    }

    private async Task<string?> WaitForCopiedTextAsync(int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (_clipboardService.TryGetText(out var text) &&
                !string.IsNullOrEmpty(text))
            {
                return text;
            }

            await Task.Delay(35);
        }

        return null;
    }

    private static bool IsProtectedProcess(IntPtr foregroundWindow)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
            using var process = Process.GetProcessById((int)processId);
            var processName = SettingsService.NormalizeProcessName(process.ProcessName);
            return ProtectedProcessNames.Contains(processName);
        }
        catch
        {
            return true;
        }
    }

    private void Restore(ClipboardSnapshot snapshot)
    {
        _clipboardService.TryRestore(snapshot);
    }
}
