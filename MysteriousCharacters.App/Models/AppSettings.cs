namespace MysteriousCharacters.App.Models;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    public bool RestoreClipboard { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    public int CopyTimeoutMilliseconds { get; set; } = 1000;

    public int CopyRetryCount { get; set; } = 2;

    public int ModifierReleaseTimeoutMilliseconds { get; set; } = 700;

    public int ClipboardRestoreDelayMilliseconds { get; set; } = 650;

    public string? CustomDictionaryPath { get; set; }

}
