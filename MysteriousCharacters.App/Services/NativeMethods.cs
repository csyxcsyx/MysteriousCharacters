using System.Runtime.InteropServices;
using System.Text;

namespace MysteriousCharacters.App.Services;

internal static class NativeMethods
{
    private const int GwlStyle = -16;
    private const int EsMultiline = 0x0004;
    private const int EsPassword = 0x0020;
    private const int MaxDirectTextLength = 4 * 1024 * 1024;

    public const int WmHotkey = 0x0312;
    public const uint WmGetText = 0x000D;
    public const uint WmGetTextLength = 0x000E;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModNoRepeat = 0x4000;
    public const uint WmCopy = 0x0301;
    public const uint WmPaste = 0x0302;
    public const uint EmGetSel = 0x00B0;
    public const uint EmReplaceSel = 0x00C2;
    public const uint SmtoAbortIfHung = 0x0002;
    public const uint InputKeyboard = 1;
    public const uint KeyEventKeyUp = 0x0002;
    public const ushort VkControl = 0x11;
    public const ushort VkShift = 0x10;
    public const ushort VkAlt = 0x12;
    public const ushort VkLeftWin = 0x5B;
    public const ushort VkRightWin = 0x5C;
    public const ushort VkC = 0x43;
    public const ushort VkD = 0x44;
    public const ushort VkE = 0x45;
    public const ushort VkInsert = 0x2D;
    public const ushort VkV = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo guiThreadInfo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        UIntPtr wordParameter,
        IntPtr longParameter,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutWithTextBuffer(
        IntPtr windowHandle,
        uint message,
        UIntPtr wordParameter,
        StringBuilder longParameter,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutWithText(
        IntPtr windowHandle,
        uint message,
        UIntPtr wordParameter,
        string longParameter,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutWithSelection(
        IntPtr windowHandle,
        uint message,
        ref uint wordParameter,
        ref uint longParameter,
        uint flags,
        uint timeout,
        out UIntPtr result);

    public static bool AreModifierKeysReleased()
    {
        return !IsPressed(VkControl) &&
               !IsPressed(VkShift) &&
               !IsPressed(VkAlt) &&
               !IsPressed(VkLeftWin) &&
               !IsPressed(VkRightWin);
    }

    public static bool AreHotkeyKeysReleased(ushort triggerKey)
    {
        return AreModifierKeysReleased() && !IsPressed(triggerKey);
    }

    public static bool SendShortcut(ushort key, ushort modifier = VkControl)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(modifier, false),
            CreateKeyboardInput(key, false),
            CreateKeyboardInput(key, true),
            CreateKeyboardInput(modifier, true)
        };

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    public static bool TryCopyFocusedControl(IntPtr foregroundWindow)
    {
        if (!TryGetFocusedControl(foregroundWindow, out var controlHandle) ||
            !IsSupportedClipboardMessageControl(controlHandle))
        {
            return false;
        }

        return SendMessageTimeout(
            controlHandle,
            WmCopy,
            UIntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            300,
            out _) != IntPtr.Zero;
    }

    public static bool TryPasteFocusedControl(IntPtr foregroundWindow)
    {
        if (!TryGetFocusedControl(foregroundWindow, out var controlHandle) ||
            !IsSupportedClipboardMessageControl(controlHandle))
        {
            return false;
        }

        return SendMessageTimeout(
            controlHandle,
            WmPaste,
            UIntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            300,
            out _) != IntPtr.Zero;
    }

    public static bool TryReadFocusedTextSelection(
        IntPtr foregroundWindow,
        out FocusedTextSelection selection)
    {
        selection = default;
        if (!TryGetFocusedControl(foregroundWindow, out var controlHandle) ||
            !IsSupportedDirectEditControl(controlHandle) ||
            IsPasswordControl(controlHandle) ||
            !TryGetSelectionRange(controlHandle, out var start, out var end) ||
            end <= start)
        {
            return false;
        }

        if (SendMessageTimeout(
                controlHandle,
                WmGetTextLength,
                UIntPtr.Zero,
                IntPtr.Zero,
                SmtoAbortIfHung,
                300,
                out var textLengthResult) == IntPtr.Zero)
        {
            return false;
        }

        var textLength = checked((long)textLengthResult.ToUInt64());
        if (textLength < end || textLength > MaxDirectTextLength)
        {
            return false;
        }

        var buffer = new StringBuilder(checked((int)textLength + 1));
        if (SendMessageTimeoutWithTextBuffer(
                controlHandle,
                WmGetText,
                (UIntPtr)buffer.Capacity,
                buffer,
                SmtoAbortIfHung,
                300,
                out _) == IntPtr.Zero ||
            end > buffer.Length)
        {
            return false;
        }

        var selectedText = buffer.ToString(
            checked((int)start),
            checked((int)(end - start)));
        if (selectedText.Contains('\r') || selectedText.Contains('\n'))
        {
            return false;
        }

        selection = new FocusedTextSelection(
            foregroundWindow,
            controlHandle,
            start,
            end,
            selectedText);
        return true;
    }

    public static bool TryReplaceFocusedTextSelection(
        FocusedTextSelection selection,
        string replacement)
    {
        if (selection.ForegroundWindow != GetForegroundWindow() ||
            !TryGetFocusedControl(selection.ForegroundWindow, out var controlHandle) ||
            controlHandle != selection.ControlHandle ||
            !TryGetSelectionRange(controlHandle, out var start, out var end) ||
            start != selection.Start ||
            end != selection.End)
        {
            return false;
        }

        return SendMessageTimeoutWithText(
            controlHandle,
            EmReplaceSel,
            new UIntPtr(1),
            replacement,
            SmtoAbortIfHung,
            300,
            out _) != IntPtr.Zero;
    }

    private static bool TryGetFocusedControl(IntPtr foregroundWindow, out IntPtr controlHandle)
    {
        controlHandle = IntPtr.Zero;
        if (foregroundWindow == IntPtr.Zero || foregroundWindow != GetForegroundWindow())
        {
            return false;
        }

        var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
        var info = new GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };
        if (threadId == 0 || !GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        controlHandle = info.Focus != IntPtr.Zero ? info.Focus : foregroundWindow;
        return controlHandle != IntPtr.Zero;
    }

    private static bool IsSupportedDirectEditControl(IntPtr controlHandle)
    {
        return TryGetControlClassName(controlHandle, out var className) &&
               string.Equals(className, "Edit", StringComparison.OrdinalIgnoreCase) &&
               (GetWindowLong(controlHandle, GwlStyle) & EsMultiline) == 0;
    }

    private static bool IsSupportedClipboardMessageControl(IntPtr controlHandle)
    {
        return TryGetControlClassName(controlHandle, out var className) &&
               (string.Equals(className, "Edit", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetControlClassName(IntPtr controlHandle, out string className)
    {
        var buffer = new StringBuilder(128);
        if (GetClassName(controlHandle, buffer, buffer.Capacity) == 0)
        {
            className = string.Empty;
            return false;
        }

        className = buffer.ToString();
        return true;
    }

    private static bool IsPasswordControl(IntPtr controlHandle)
    {
        return (GetWindowLong(controlHandle, GwlStyle) & EsPassword) != 0;
    }

    private static bool TryGetSelectionRange(IntPtr controlHandle, out uint start, out uint end)
    {
        start = 0;
        end = 0;
        return SendMessageTimeoutWithSelection(
            controlHandle,
            EmGetSel,
            ref start,
            ref end,
            SmtoAbortIfHung,
            300,
            out _) != IntPtr.Zero;
    }

    private static Input CreateKeyboardInput(ushort key, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyEventKeyUp : 0
                }
            }
        };
    }

    private static bool IsPressed(ushort key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public readonly record struct FocusedTextSelection(
        IntPtr ForegroundWindow,
        IntPtr ControlHandle,
        uint Start,
        uint End,
        string Text);

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
