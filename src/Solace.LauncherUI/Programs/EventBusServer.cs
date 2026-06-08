using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Solace.LauncherUI.Programs;

internal static class EventBusServer
{
    public static readonly string ExeName = "EventBusServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DisplayName = "EventBus server";

#pragma warning disable IDE0060 // Remove unused parameter
    public static bool Check(Settings settings, ILogger logger)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        string exePath = Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName));
        if (!File.Exists(exePath))
        {
            ProgramsLogs.LogExecutableNotFound(logger, DisplayName, exePath);
            return false;
        }

        return true;
    }

    public static Process? Run(Settings settings, ILogger logger)
    {
        ProgramsLogs.LogRunning(logger, DisplayName);
        return Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName)),
        [
            $"--port={settings.EventBusPort}",
            $"--logger-url={Program.LoggerAddress}",
        ])
        {
            WorkingDirectory = Path.GetFullPath(Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true,
        });
    }
}
