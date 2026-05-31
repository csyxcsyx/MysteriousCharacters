using System.IO;
using System.Text.Json;
using MysteriousCharacters.App.Models;

namespace MysteriousCharacters.App.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public SettingsService(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MysteriousCharacters");
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
    }

    public string DataDirectory { get; }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(DataDirectory);

        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, _jsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.CopyTimeoutMilliseconds = Math.Clamp(settings.CopyTimeoutMilliseconds, 300, 3000);
        settings.CopyRetryCount = Math.Clamp(settings.CopyRetryCount, 1, 4);
        settings.ModifierReleaseTimeoutMilliseconds = Math.Clamp(
            settings.ModifierReleaseTimeoutMilliseconds,
            200,
            2000);
        settings.ClipboardRestoreDelayMilliseconds = Math.Clamp(
            settings.ClipboardRestoreDelayMilliseconds,
            100,
            3000);
    }

    public static string NormalizeProcessName(string processName)
    {
        var value = processName.Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }
}
