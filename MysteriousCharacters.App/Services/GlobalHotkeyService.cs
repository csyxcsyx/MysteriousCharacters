using System.Windows.Input;
using System.Windows.Interop;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D43;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private HotkeyGesture? _registeredGesture;
    private bool _disposed;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法初始化快捷键消息窗口。");
        _source.AddHook(WindowProcedure);
    }

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyGesture gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_registeredGesture?.Equals(gesture) == true)
        {
            return true;
        }

        var previous = _registeredGesture;
        UnregisterCurrent();

        if (Register(gesture))
        {
            _registeredGesture = gesture;
            return true;
        }

        if (previous is not null && Register(previous))
        {
            _registeredGesture = previous;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterCurrent();
        _source.RemoveHook(WindowProcedure);
        _disposed = true;
    }

    private bool Register(HotkeyGesture gesture)
    {
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
        var modifiers = (uint)gesture.Modifiers | NativeMethods.ModNoRepeat;
        return NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, modifiers, virtualKey);
    }

    private void UnregisterCurrent()
    {
        if (_registeredGesture is null)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
        _registeredGesture = null;
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wordParameter.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }
}
