using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Solace.AdminPanel.Programs;

internal static class ApiServer
{
    public static readonly string ExeName = "ApiServer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DisplayName = "Api server";

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

        var arguments = new List<string>(7)
        {
            $"--port={settings.ApiPort}",
            $"--earth-db={settings.EarthDatabaseConnectionString}",
            $"--eventbus=localhost:{settings.EventBusPort}",
            $"--objectstore=localhost:{settings.ObjectStorePort}",
            $"--logger-url={Program.LoggerAddress}",
            $"--dir={Program.StaticDataDir}",
        };

        if (settings.OnlyAllowLocalLogin is true)
        {
            arguments.Add($"--local-login-only");
        }

        return Process.Start(new ProcessStartInfo(Path.GetFullPath(Path.Combine(Program.ProgramsDir, ExeName)), arguments)
        {
            WorkingDirectory = Path.GetFullPath(Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true,
        });
    }
}
