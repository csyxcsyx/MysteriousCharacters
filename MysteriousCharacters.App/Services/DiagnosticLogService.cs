using System.IO;
using System.Runtime.InteropServices;

namespace MysteriousCharacters.App.Services;

public sealed class DiagnosticLogService
{
    private const long MaxLogLength = 512 * 1024;
    private readonly object _sync = new();

    public DiagnosticLogService(string dataDirectory)
    {
        LogPath = Path.Combine(dataDirectory, "diagnostics.log");
    }

    public string LogPath { get; }

    public void Write(string eventName, string details)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogLength)
                {
                    File.WriteAllText(LogPath, string.Empty);
                }

                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O}\t{eventName}\t{Sanitize(details)}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never interfere with text replacement.
        }
    }

    public void WriteRuntime()
    {
        Write(
            "runtime",
            $"os={Environment.OSVersion}; " +
            $"os_arch={RuntimeInformation.OSArchitecture}; " +
            $"process_arch={RuntimeInformation.ProcessArchitecture}; " +
            $"framework={RuntimeInformation.FrameworkDescription}");
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }
}
