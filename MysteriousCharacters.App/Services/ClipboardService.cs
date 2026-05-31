using System.Runtime.InteropServices;
using System.Windows;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.DataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace MysteriousCharacters.App.Services;

public sealed class ClipboardSnapshot
{
    public ClipboardSnapshot(WpfDataObject? data)
    {
        Data = data;
    }

    public WpfDataObject? Data { get; }
}

public sealed class ClipboardService
{
    private const int RetryCount = 6;
    private const int RetryDelayMilliseconds = 35;

    public bool TryCapture(out ClipboardSnapshot? snapshot)
    {
        ClipboardSnapshot? captured = null;
        var succeeded = TryClipboardOperation(() =>
        {
            var source = WpfClipboard.GetDataObject();
            if (source is null)
            {
                captured = new ClipboardSnapshot(null);
                return;
            }

            var clone = new WpfDataObject();
            foreach (var format in source.GetFormats(false))
            {
                try
                {
                    var value = source.GetData(format, false);
                    if (value is not null)
                    {
                        clone.SetData(format, value);
                    }
                }
                catch
                {
                    // Some clipboard providers advertise formats that cannot be materialized.
                }
            }

            captured = new ClipboardSnapshot(clone);
        });

        snapshot = captured;
        return succeeded;
    }

    public bool TryClear()
    {
        return TryClipboardOperation(WpfClipboard.Clear);
    }

    public bool TryGetText(out string? text)
    {
        string? clipboardText = null;
        var succeeded = TryClipboardOperation(() =>
        {
            clipboardText = WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText)
                ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
                : null;
        });

        text = clipboardText;
        return succeeded;
    }

    public bool TrySetText(string text)
    {
        return TryClipboardOperation(() => WpfClipboard.SetText(text, WpfTextDataFormat.UnicodeText));
    }

    public bool TryRestore(ClipboardSnapshot snapshot)
    {
        return TryClipboardOperation(() =>
        {
            if (snapshot.Data is null)
            {
                WpfClipboard.Clear();
            }
            else
            {
                WpfClipboard.SetDataObject(snapshot.Data, true);
            }
        });
    }

    private static bool TryClipboardOperation(Action operation)
    {
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            try
            {
                operation();
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }

        return false;
    }
}
