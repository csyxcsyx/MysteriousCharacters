using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace MysteriousCharacters.App.Services;

public sealed class UiAutomationTextSelection
{
    public UiAutomationTextSelection(int[] runtimeId, string text)
    {
        RuntimeId = runtimeId;
        Text = text;
    }

    public int[] RuntimeId { get; }

    public string Text { get; }
}

public sealed class UiAutomationSelectionService
{
    private const int RetryCount = 3;
    private const int RetryDelayMilliseconds = 30;

    public string? LastFailureReason { get; private set; }

    public bool TryReadSelection(
        IntPtr foregroundWindow,
        out UiAutomationTextSelection? selection)
    {
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            if (TryReadSelectionOnce(foregroundWindow, out selection))
            {
                return true;
            }

            if (attempt + 1 < RetryCount)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }

        selection = null;
        return false;
    }

    private bool TryReadSelectionOnce(
        IntPtr foregroundWindow,
        out UiAutomationTextSelection? selection)
    {
        selection = null;
        LastFailureReason = null;

        try
        {
            var element = GetFocusedElement(foregroundWindow);
            if (element is null)
            {
                LastFailureReason = "focused-element-unavailable";
                return false;
            }

            if (!TryGetTextPattern(element, out var textPattern))
            {
                LastFailureReason = "text-pattern-unavailable";
                return false;
            }

            if (!TryGetSingleSelection(textPattern, out var selectedRange))
            {
                LastFailureReason = "single-selection-unavailable";
                return false;
            }

            var text = selectedRange.GetText(-1);
            if (string.IsNullOrEmpty(text))
            {
                LastFailureReason = "selection-empty";
                return false;
            }

            selection = new UiAutomationTextSelection(element.GetRuntimeId(), text);
            return true;
        }
        catch (Exception exception)
        {
            // Providers live in external applications. A broken or unavailable
            // provider must never prevent the clipboard fallback from running.
            LastFailureReason = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public bool TryReplaceSelection(
        IntPtr foregroundWindow,
        UiAutomationTextSelection selection,
        string replacement)
    {
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            if (TryReplaceSelectionOnce(foregroundWindow, selection, replacement))
            {
                return true;
            }

            if (attempt + 1 < RetryCount)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }

        return false;
    }

    private static bool TryReplaceSelectionOnce(
        IntPtr foregroundWindow,
        UiAutomationTextSelection selection,
        string replacement)
    {
        try
        {
            var element = GetFocusedElement(foregroundWindow);
            if (element is null ||
                !element.GetRuntimeId().SequenceEqual(selection.RuntimeId) ||
                !TryGetTextPattern(element, out var textPattern) ||
                !TryGetSingleSelection(textPattern, out var selectedRange) ||
                !element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) ||
                valuePatternObject is not ValuePattern valuePattern ||
                valuePattern.Current.IsReadOnly)
            {
                return false;
            }

            var selectedText = selectedRange.GetText(-1);
            if (!string.Equals(selectedText, selection.Text, StringComparison.Ordinal))
            {
                return false;
            }

            var documentRange = textPattern.DocumentRange;
            var beforeRange = documentRange.Clone();
            beforeRange.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                selectedRange,
                TextPatternRangeEndpoint.Start);

            var afterRange = documentRange.Clone();
            afterRange.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                selectedRange,
                TextPatternRangeEndpoint.End);

            var beforeText = beforeRange.GetText(-1);
            var afterText = afterRange.GetText(-1);
            var currentValue = valuePattern.Current.Value;
            if (!string.Equals(
                    currentValue,
                    beforeText + selectedText + afterText,
                    StringComparison.Ordinal))
            {
                return false;
            }

            valuePattern.SetValue(beforeText + replacement + afterText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? GetFocusedElement(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero ||
            foregroundWindow != NativeMethods.GetForegroundWindow())
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        var element = AutomationElement.FocusedElement;
        if (element is null ||
            foregroundProcessId == 0 ||
            !BelongsToForegroundWindow(element, foregroundWindow, foregroundProcessId) ||
            element.Current.IsPassword)
        {
            return null;
        }

        return element;
    }

    private static bool BelongsToForegroundWindow(
        AutomationElement element,
        IntPtr foregroundWindow,
        uint foregroundProcessId)
    {
        try
        {
            var root = AutomationElement.FromHandle(foregroundWindow);
            var rootRuntimeId = root.GetRuntimeId();
            AutomationElement? current = element;

            for (var depth = 0; current is not null && depth < 64; depth++)
            {
                if (current.GetRuntimeId().SequenceEqual(rootRuntimeId))
                {
                    return true;
                }

                current = TreeWalker.RawViewWalker.GetParent(current);
            }
        }
        catch
        {
            // Some providers do not expose a traversable raw tree.
        }

        return element.Current.ProcessId == checked((int)foregroundProcessId);
    }

    private static bool TryGetTextPattern(
        AutomationElement element,
        out TextPattern textPattern)
    {
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) &&
            patternObject is TextPattern pattern)
        {
            textPattern = pattern;
            return true;
        }

        textPattern = null!;
        return false;
    }

    private static bool TryGetSingleSelection(
        TextPattern textPattern,
        out TextPatternRange selectedRange)
    {
        var selections = textPattern.GetSelection();
        if (selections.Length == 1)
        {
            selectedRange = selections[0];
            return true;
        }

        selectedRange = null!;
        return false;
    }
}
