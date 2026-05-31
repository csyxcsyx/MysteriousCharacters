using System.Windows.Interop;

namespace MysteriousCharacters.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int EncodeHotkeyId = 0x4D43;
    private const int DecodeHotkeyId = 0x4D44;
    private const uint EncodeVirtualKey = 0x45;
    private const uint DecodeVirtualKey = 0x44;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("无法初始化快捷键消息窗口。");
        _source.AddHook(WindowProcedure);
    }

    public event Action<TransformDirection>? Pressed;

    public bool TryRegister()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_registered)
        {
            return true;
        }

        if (!Register(EncodeHotkeyId, EncodeVirtualKey))
        {
            return false;
        }

        if (!Register(DecodeHotkeyId, DecodeVirtualKey))
        {
            NativeMethods.UnregisterHotKey(_windowHandle, EncodeHotkeyId);
            return false;
        }

        _registered = true;
        return true;
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

    private bool Register(int id, uint virtualKey)
    {
        var modifiers =
            NativeMethods.ModControl |
            NativeMethods.ModAlt |
            NativeMethods.ModShift |
            NativeMethods.ModNoRepeat;
        return NativeMethods.RegisterHotKey(_windowHandle, id, modifiers, virtualKey);
    }

    private void UnregisterCurrent()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, EncodeHotkeyId);
        NativeMethods.UnregisterHotKey(_windowHandle, DecodeHotkeyId);
        _registered = false;
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message != NativeMethods.WmHotkey)
        {
            return IntPtr.Zero;
        }

        var direction = wordParameter.ToInt32() switch
        {
            EncodeHotkeyId => TransformDirection.Encode,
            DecodeHotkeyId => TransformDirection.Decode,
            _ => (TransformDirection?)null
        };
        if (direction is not null)
        {
            handled = true;
            Pressed?.Invoke(direction.Value);
        }

        return IntPtr.Zero;
    }
}
