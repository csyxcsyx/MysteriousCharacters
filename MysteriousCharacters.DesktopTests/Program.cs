using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Input;
using MysteriousCharacters.App.Models;
using MysteriousCharacters.App.Services;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;

namespace MysteriousCharacters.DesktopTests;

internal static class Program
{
    private const string HostTitlePrefix = "MysteriousCharacters.DesktopTests|";
    private const string MultilineSource =
        "原文：我看着你的脸，轻刷着和弦；情人节卡片，手写的从前......\r\n" +
        "原文：天青色等烟雨，而我在等你......";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--published-app", var executablePath])
        {
            VerifyPublishedAppHotkeyAsync(executablePath).GetAwaiter().GetResult();
            return;
        }

        if (args is ["--host", var framework, var encodedText])
        {
            RunHost(framework, Decode(encodedText));
            return;
        }

        RunTestsAsync().GetAwaiter().GetResult();
    }

    private static async Task RunTestsAsync()
    {
        var dictionaryService = new DictionaryService();
        var transformer = new TextTransformer(dictionaryService.LoadRules(null));
        var encoded = transformer.Transform(MultilineSource);
        var decoded = transformer.Transform(encoded, TransformDirection.Decode);

        await VerifyReplacementAsync(
            "winforms",
            MultilineSource,
            TransformDirection.Encode,
            encoded,
            false);
        await VerifyReplacementAsync("wpf", MultilineSource, TransformDirection.Encode, encoded, true);
        await VerifyReplacementAsync("wpf", encoded, TransformDirection.Decode, decoded, true);

        Console.WriteLine("desktop_tests=passed");
    }

    private static async Task VerifyPublishedAppHotkeyAsync(string executablePath)
    {
        var transformer = new TextTransformer(new DictionaryService().LoadRules(null));
        var expected = transformer.Transform(MultilineSource);
        using var app = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Unable to start the published app.");

        try
        {
            await WaitForMainWindowAsync(app);
            var scenarios = new[]
            {
                new
                {
                    Direction = TransformDirection.Encode,
                    Source = MultilineSource,
                    Expected = expected
                },
                new
                {
                    Direction = TransformDirection.Decode,
                    Source = expected,
                    Expected = transformer.Transform(expected, TransformDirection.Decode)
                }
            };

            foreach (var scenario in scenarios)
            {
                using var host = StartHost("wpf", scenario.Source);
                try
                {
                var hostWindow = await WaitForMainWindowAsync(host);
                await WaitUntilAsync(
                    () =>
                    {
                        ActivateWindow(hostWindow);
                        return GetForegroundWindow() == hostWindow;
                    },
                    "The published-app host window did not become foreground.");

                    PressSelectAll();
                    PressHotkey(scenario.Direction);
                    await WaitUntilAsync(
                        () => string.Equals(ReadHostText(host), scenario.Expected, StringComparison.Ordinal),
                        $"The published app did not replace the selected WPF text after {scenario.Direction}.");
                    Console.WriteLine(
                        $"published_app_hotkey_{scenario.Direction.ToString().ToLowerInvariant()}=passed");
                }
                finally
                {
                    if (!host.HasExited)
                    {
                        host.Kill(true);
                        host.WaitForExit();
                    }
                }
            }
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(true);
                app.WaitForExit();
            }
        }
    }

    private static async Task VerifyReplacementAsync(
        string framework,
        string source,
        TransformDirection direction,
        string expected,
        bool requireUiAutomationSelection)
    {
        using var host = StartHost(framework, source);
        try
        {
            var windowHandle = await WaitForMainWindowAsync(host);
            await WaitUntilAsync(
                () =>
                {
                    ActivateWindow(windowHandle);
                    return GetForegroundWindow() == windowHandle;
                },
                $"The {framework} host window did not become foreground. " +
                $"Target={windowHandle}; Foreground={GetForegroundWindow()}.");

            PressSelectAll();
            var notifications = new List<string>();
            var uiAutomationSelectionService = new UiAutomationSelectionService();
            if (requireUiAutomationSelection)
            {
                var readSelection = uiAutomationSelectionService.TryReadSelection(
                    windowHandle,
                    out var selection);
                Assert(
                    readSelection &&
                    selection is not null &&
                    string.Equals(selection.Text, source, StringComparison.Ordinal),
                    "UI Automation did not expose the complete " +
                    $"{framework} selection. Read={readSelection}; " +
                    $"Selection={Escape(selection?.Text)}; Source={Escape(source)}; " +
                    $"Failure={uiAutomationSelectionService.LastFailureReason}; " +
                    $"Focused={DescribeFocusedAutomationElement()}");
            }

            var coordinator = new TextReplacementCoordinator(
                new ClipboardService(),
                uiAutomationSelectionService,
                new TextTransformer(new DictionaryService().LoadRules(null)),
                () => new AppSettings
                {
                    Enabled = true,
                    RestoreClipboard = true,
                    ShowNotifications = false,
                    ClipboardRestoreDelayMilliseconds = 100
                },
                (title, message) => notifications.Add($"{title}: {message}"));

            await coordinator.TryReplaceSelectionAsync(direction);
            await WaitUntilAsync(
                () => string.Equals(ReadHostText(host), expected, StringComparison.Ordinal),
                $"{framework} replacement failed. Notifications: {string.Join(" | ", notifications)}");

            Console.WriteLine($"desktop_{framework}_{direction.ToString().ToLowerInvariant()}=passed");
        }
        finally
        {
            if (!host.HasExited)
            {
                host.Kill(true);
                host.WaitForExit();
            }
        }
    }

    private static Process StartHost(string framework, string text)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the desktop test executable path.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(framework);
        startInfo.ArgumentList.Add(Encode(text));

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start the {framework} desktop test host.");
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process host)
    {
        IntPtr handle = IntPtr.Zero;
        await WaitUntilAsync(() =>
        {
            host.Refresh();
            handle = host.MainWindowHandle;
            return handle != IntPtr.Zero;
        }, "The desktop test host did not create a main window.");
        return handle;
    }

    private static string? ReadHostText(Process host)
    {
        host.Refresh();
        var title = host.MainWindowTitle;
        return title.StartsWith(HostTitlePrefix, StringComparison.Ordinal)
            ? Decode(title[HostTitlePrefix.Length..])
            : null;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string failureMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static void RunHost(string framework, string text)
    {
        switch (framework)
        {
            case "winforms":
                RunWinFormsHost(text);
                return;
            case "wpf":
                RunWpfHost(text);
                return;
            default:
                throw new InvalidOperationException($"Unknown desktop host framework: {framework}");
        }
    }

    private static void RunWinFormsHost(string text)
    {
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);
        using var form = new Forms.Form
        {
            Width = 720,
            Height = 360,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };
        using var editor = new Forms.TextBox
        {
            Dock = Forms.DockStyle.Fill,
            Multiline = true,
            Text = text
        };

        void UpdateTitle() => form.Text = HostTitlePrefix + Encode(editor.Text);

        editor.TextChanged += (_, _) => UpdateTitle();
        form.Controls.Add(editor);
        form.Shown += (_, _) =>
        {
            form.Activate();
            editor.Focus();
            editor.SelectAll();
            SetForegroundWindow(form.Handle);
            UpdateTitle();
        };
        UpdateTitle();
        Forms.Application.Run(form);
    }

    private static void RunWpfHost(string text)
    {
        var app = new Wpf.Application();
        var editor = new WpfControls.TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Text = text,
            TextWrapping = Wpf.TextWrapping.Wrap
        };
        var window = new Wpf.Window
        {
            Width = 720,
            Height = 360,
            Content = editor
        };

        void UpdateTitle() => window.Title = HostTitlePrefix + Encode(editor.Text);
        void FocusEditor()
        {
            editor.Focus();
            Keyboard.Focus(editor);
            editor.SelectAll();
        }

        editor.TextChanged += (_, _) => UpdateTitle();
        window.ContentRendered += (_, _) =>
        {
            window.Activate();
            FocusEditor();
            SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            UpdateTitle();
        };
        window.Activated += (_, _) => FocusEditor();
        UpdateTitle();
        app.Run(window);
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static string DescribeFocusedAutomationElement()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return "<null>";
            }

            var patterns = element
                .GetSupportedPatterns()
                .Select(pattern => pattern.ProgrammaticName);
            var textSelection = "<not-supported>";
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
                textPatternObject is TextPattern textPattern)
            {
                var selections = textPattern.GetSelection();
                textSelection = string.Join(
                    ",",
                    selections.Select(selection => Escape(selection.GetText(-1))));
            }

            return $"Class={element.Current.ClassName}; Patterns={string.Join(",", patterns)}; " +
                   $"ProcessId={element.Current.ProcessId}; RuntimeId={string.Join(",", element.GetRuntimeId())}; " +
                   $"Selection={textSelection}";
        }
        catch (Exception exception)
        {
            return $"<{exception.GetType().Name}: {exception.Message}>";
        }
    }

    private static string Escape(string? value)
    {
        return value is null
            ? "<null>"
            : value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ActivateWindow(IntPtr windowHandle)
    {
        KeybdEvent(0x12, 0, 0, UIntPtr.Zero);
        KeybdEvent(0x12, 0, 0x0002, UIntPtr.Zero);
        ShowWindow(windowHandle, 9);
        ClickWindowCenter(windowHandle);

        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        var targetThread = GetWindowThreadProcessId(windowHandle, IntPtr.Zero);
        var currentThread = GetCurrentThreadId();
        var attachedForeground = foregroundThread != 0 &&
                                 foregroundThread != currentThread &&
                                 AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != 0 &&
                             targetThread != currentThread &&
                             targetThread != foregroundThread &&
                             AttachThreadInput(currentThread, targetThread, true);

        try
        {
            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
            SetActiveWindow(windowHandle);
        }
        finally
        {
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static void ClickWindowCenter(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var rect))
        {
            return;
        }

        SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        MouseEvent(0x0002, 0, 0, 0, UIntPtr.Zero);
        MouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    private static void PressSelectAll()
    {
        KeybdEvent(0x11, 0, 0, UIntPtr.Zero);
        KeybdEvent(0x41, 0, 0, UIntPtr.Zero);
        KeybdEvent(0x41, 0, 0x0002, UIntPtr.Zero);
        KeybdEvent(0x11, 0, 0x0002, UIntPtr.Zero);
    }

    private static void PressHotkey(TransformDirection direction)
    {
        var virtualKey = direction == TransformDirection.Encode ? (byte)0x45 : (byte)0x44;

        KeybdEvent(0x11, 0, 0, UIntPtr.Zero);
        KeybdEvent(0x12, 0, 0, UIntPtr.Zero);
        KeybdEvent(virtualKey, 0, 0, UIntPtr.Zero);
        KeybdEvent(virtualKey, 0, 0x0002, UIntPtr.Zero);
        KeybdEvent(0x12, 0, 0x0002, UIntPtr.Zero);
        KeybdEvent(0x11, 0, 0x0002, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint firstThread, uint secondThread, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(
        uint flags,
        uint x,
        uint y,
        uint data,
        UIntPtr extraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
