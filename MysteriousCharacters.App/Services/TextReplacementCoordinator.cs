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
    private readonly UiAutomationSelectionService _uiAutomationSelectionService;
    private readonly TextTransformer _transformer;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action<string, string> _notify;
    private readonly DiagnosticLogService? _diagnosticLogService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public TextReplacementCoordinator(
        ClipboardService clipboardService,
        UiAutomationSelectionService uiAutomationSelectionService,
        TextTransformer transformer,
        Func<AppSettings> settingsProvider,
        Action<string, string> notify,
        DiagnosticLogService? diagnosticLogService = null)
    {
        _clipboardService = clipboardService;
        _uiAutomationSelectionService = uiAutomationSelectionService;
        _transformer = transformer;
        _settingsProvider = settingsProvider;
        _notify = notify;
        _diagnosticLogService = diagnosticLogService;
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
                WriteDiagnostic("replacement-skipped", $"direction={direction}; reason=disabled");
                return;
            }

            var foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero || IsProtectedProcess(foregroundWindow))
            {
                WriteDiagnostic(
                    "replacement-skipped",
                    $"direction={direction}; reason=missing-or-protected-foreground");
                return;
            }

            WriteDiagnostic(
                "replacement-started",
                $"direction={direction}; foreground=0x{foregroundWindow.ToInt64():X}; " +
                $"process={GetForegroundProcessDescription(foregroundWindow)}");

            var triggerKey = direction == TransformDirection.Encode
                ? NativeMethods.VkE
                : NativeMethods.VkD;
            if (!await WaitForHotkeyKeysReleasedAsync(
                    triggerKey,
                    settings.ModifierReleaseTimeoutMilliseconds))
            {
                WriteDiagnostic("replacement-skipped", $"direction={direction}; reason=hotkey-still-pressed");
                _notify("快捷键仍处于按下状态", "请松开快捷键后重试。");
                return;
            }

            if (foregroundWindow != NativeMethods.GetForegroundWindow())
            {
                WriteDiagnostic("replacement-skipped", $"direction={direction}; reason=foreground-changed");
                return;
            }

            if (await TryReplaceUsingUiAutomationAsync(settings, direction, foregroundWindow))
            {
                return;
            }

            if (TryReplaceFocusedTextSelection(settings, direction, foregroundWindow))
            {
                WriteDiagnostic("replacement-completed", $"direction={direction}; strategy=win32-direct");
                return;
            }

            if (!_clipboardService.TryCapture(out var snapshot) || snapshot is null)
            {
                WriteDiagnostic("replacement-failed", $"direction={direction}; reason=clipboard-capture");
                _notify("剪贴板暂时不可用", "无法保存当前剪贴板内容，本次转换已跳过。");
                return;
            }

            var selectedText = await TryCopySelectionAsync(settings, foregroundWindow);
            if (selectedText is null)
            {
                Restore(snapshot);
                WriteDiagnostic(
                    "replacement-failed",
                    $"direction={direction}; reason=selection-copy; " +
                    $"uia={_uiAutomationSelectionService.LastFailureReason}");
                _notify(
                    "没有读取到选中文本",
                    "请确认已选中文字。若目标软件以管理员身份运行，请以相同权限启动隐文匣。");
                return;
            }

            var transformedText = _transformer.Transform(selectedText, direction);
            if (string.Equals(selectedText, transformedText, StringComparison.Ordinal))
            {
                Restore(snapshot);
                NotifyNoTransformation(direction);
                return;
            }

            if (!await TryPasteTextAsync(transformedText, foregroundWindow))
            {
                Restore(snapshot);
                WriteDiagnostic("replacement-failed", $"direction={direction}; reason=clipboard-paste");
                _notify("粘贴失败", "目标窗口可能受权限或安全策略保护，本次转换已跳过。");
                return;
            }

            await RestoreClipboardAfterPasteAsync(settings, snapshot);
            WriteDiagnostic("replacement-completed", $"direction={direction}; strategy=clipboard-copy-paste");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<bool> TryReplaceUsingUiAutomationAsync(
        AppSettings settings,
        TransformDirection direction,
        IntPtr foregroundWindow)
    {
        if (!_uiAutomationSelectionService.TryReadSelection(foregroundWindow, out var selection) ||
            selection is null)
        {
            WriteDiagnostic(
                "uia-selection-unavailable",
                $"direction={direction}; reason={_uiAutomationSelectionService.LastFailureReason}");
            return false;
        }

        var transformedText = _transformer.Transform(selection.Text, direction);
        if (string.Equals(selection.Text, transformedText, StringComparison.Ordinal))
        {
            NotifyNoTransformation(direction);
            return true;
        }

        if (_uiAutomationSelectionService.TryReplaceSelection(
                foregroundWindow,
                selection,
                transformedText))
        {
            if (!settings.RestoreClipboard)
            {
                _clipboardService.TrySetText(transformedText);
            }

            WriteDiagnostic("replacement-completed", $"direction={direction}; strategy=uia-direct");
            return true;
        }

        if (!_clipboardService.TryCapture(out var snapshot) || snapshot is null)
        {
            WriteDiagnostic("replacement-failed", $"direction={direction}; reason=uia-clipboard-capture");
            _notify("剪贴板暂时不可用", "无法保存当前剪贴板内容，本次转换已跳过。");
            return true;
        }

        if (!await TryPasteTextAsync(transformedText, foregroundWindow))
        {
            Restore(snapshot);
            WriteDiagnostic("replacement-failed", $"direction={direction}; reason=uia-clipboard-paste");
            _notify("粘贴失败", "目标窗口可能受权限或安全策略保护，本次转换已跳过。");
            return true;
        }

        await RestoreClipboardAfterPasteAsync(settings, snapshot);
        WriteDiagnostic("replacement-completed", $"direction={direction}; strategy=uia-read-clipboard-paste");
        return true;
    }

    private bool TryReplaceFocusedTextSelection(
        AppSettings settings,
        TransformDirection direction,
        IntPtr foregroundWindow)
    {
        if (!NativeMethods.TryReadFocusedTextSelection(foregroundWindow, out var selection))
        {
            return false;
        }

        var transformedText = _transformer.Transform(selection.Text, direction);
        if (string.Equals(selection.Text, transformedText, StringComparison.Ordinal))
        {
            NotifyNoTransformation(direction);
            return true;
        }

        if (!NativeMethods.TryReplaceFocusedTextSelection(selection, transformedText))
        {
            return false;
        }

        if (!settings.RestoreClipboard)
        {
            _clipboardService.TrySetText(transformedText);
        }

        return true;
    }

    private async Task<string?> TryCopySelectionAsync(AppSettings settings, IntPtr foregroundWindow)
    {
        var fallbackTimeout = Math.Min(settings.CopyTimeoutMilliseconds, 700);

        for (var attempt = 0; attempt < settings.CopyRetryCount; attempt++)
        {
            var copiedText = await TryCopyWithAsync(
                () => NativeMethods.TryCopyFocusedControl(foregroundWindow),
                fallbackTimeout,
                foregroundWindow);
            if (copiedText is not null)
            {
                return copiedText;
            }

            copiedText = await TryCopyWithAsync(
                () => NativeMethods.SendShortcut(NativeMethods.VkC),
                settings.CopyTimeoutMilliseconds,
                foregroundWindow);
            if (copiedText is not null)
            {
                return copiedText;
            }

            copiedText = await TryCopyWithAsync(
                () => NativeMethods.SendShortcut(NativeMethods.VkInsert),
                fallbackTimeout,
                foregroundWindow);
            if (copiedText is not null)
            {
                return copiedText;
            }

            await Task.Delay(80);
        }

        return null;
    }

    private async Task<string?> TryCopyWithAsync(
        Func<bool> copy,
        int timeoutMilliseconds,
        IntPtr foregroundWindow)
    {
        if (!_clipboardService.TryClear())
        {
            return null;
        }

        var sequenceNumber = NativeMethods.GetClipboardSequenceNumber();
        await Task.Delay(45);
        if (foregroundWindow != NativeMethods.GetForegroundWindow() || !copy())
        {
            return null;
        }

        return await WaitForCopiedTextAsync(
            sequenceNumber,
            timeoutMilliseconds,
            foregroundWindow);
    }

    private static async Task<bool> WaitForHotkeyKeysReleasedAsync(
        ushort triggerKey,
        int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (NativeMethods.AreHotkeyKeysReleased(triggerKey))
            {
                await Task.Delay(35);
                return true;
            }

            await Task.Delay(18);
        }

        return false;
    }

    private async Task<string?> WaitForCopiedTextAsync(
        uint previousSequenceNumber,
        int timeoutMilliseconds,
        IntPtr foregroundWindow)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (foregroundWindow != NativeMethods.GetForegroundWindow())
            {
                return null;
            }

            if (NativeMethods.GetClipboardSequenceNumber() != previousSequenceNumber &&
                _clipboardService.TryGetText(out var text) &&
                !string.IsNullOrEmpty(text))
            {
                return text;
            }

            await Task.Delay(35);
        }

        return null;
    }

    private async Task<bool> TryPasteTextAsync(string text, IntPtr foregroundWindow)
    {
        if (foregroundWindow != NativeMethods.GetForegroundWindow() ||
            !_clipboardService.TrySetText(text))
        {
            return false;
        }

        await Task.Delay(45);
        if (foregroundWindow != NativeMethods.GetForegroundWindow())
        {
            return false;
        }

        return NativeMethods.TryPasteFocusedControl(foregroundWindow) ||
               NativeMethods.SendShortcut(NativeMethods.VkV) ||
               NativeMethods.SendShortcut(NativeMethods.VkInsert, NativeMethods.VkShift);
    }

    private async Task RestoreClipboardAfterPasteAsync(
        AppSettings settings,
        ClipboardSnapshot snapshot)
    {
        if (!settings.RestoreClipboard)
        {
            return;
        }

        await Task.Delay(settings.ClipboardRestoreDelayMilliseconds);
        _clipboardService.TryRestore(snapshot);
    }

    private void NotifyNoTransformation(TransformDirection direction)
    {
        _notify(
            direction == TransformDirection.Encode ? "没有可转换的汉字" : "没有可还原的汉字",
            direction == TransformDirection.Encode
                ? "当前选区不包含可处理的汉字或自定义规则。"
                : "当前选区没有匹配到可尝试还原的文字。");
    }

    private void WriteDiagnostic(string eventName, string details)
    {
        _diagnosticLogService?.Write(eventName, details);
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

    private static string GetForegroundProcessDescription(IntPtr foregroundWindow)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
            using var process = Process.GetProcessById(checked((int)processId));
            return $"{SettingsService.NormalizeProcessName(process.ProcessName)}({processId})";
        }
        catch
        {
            return "unknown";
        }
    }

    private void Restore(ClipboardSnapshot snapshot)
    {
        _clipboardService.TryRestore(snapshot);
    }
}
