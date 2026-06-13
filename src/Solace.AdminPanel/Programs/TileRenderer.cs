using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Solace.AdminPanel.Programs;

internal static class TileRenderer
{
    public static readonly string ExeName = "TileRenderer" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    public const string DisplayName = "Tile renderer";

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
            settings.TileDataSource switch{
                Settings.TileDataSourceE.MapTiler => $"--maptiler_key={settings.MapTilerApiKey}",
                Settings.TileDataSourceE.PostgreSQL => $"--tileDB={settings.TileDatabaseConnectionString}",
                _ => throw new UnreachableException(),
            },
            $"--eventbus=localhost:{settings.EventBusPort}",
            $"--logger-url={Program.LoggerAddress}",
            $"--dir={Program.StaticDataDir}",
        ])
        {
            WorkingDirectory = Path.GetFullPath(Program.ProgramsDir),
            CreateNoWindow = false,
            UseShellExecute = true,
        });
    }
}
