using System.Diagnostics;
using System.Reflection;
using Solace.Common;
using Solace.Common.Utils;
using Solace.LauncherUI.Programs;

namespace Solace.LauncherUI;

internal static partial class FileChecker
{
    private static readonly HttpClient httpClient = new();

    private static readonly string[] expectedStaticFiles = [
        "catalog/itemEfficiencyCategories.json",
        "catalog/itemJournalGroups.json",
        "catalog/items.json",
        "catalog/nfc.json",
        "catalog/recipes.json",
        "catalog/recipes.json",
        "server_jars/buildplate-connector-plugin-0.0.1-SNAPSHOT-jar-with-dependencies.jar",
        "server_jars/fountain-0.0.1-SNAPSHOT-jar-with-dependencies.jar",
        "server_template_dir/mods/fountain-0.0.1.jar",
        "server_template_dir/mods/vienna-0.0.1.jar",
        "tile_renderer/tagMap1.json",
        "tile_renderer/tagMap2.json",
    ];

    private static readonly string[] expectedStaticDirectories = [
        "catalog",
        "encounters",
        "levels",
        "resourcepacks",
        "server_jars",
        "server_template_dir",
        "server_template_dir/mods",
        "tappables",
        "tile_renderer",
    ];

    static FileChecker()
    {
        bool added = httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"BitcoderCZ/Solace/{Assembly.GetExecutingAssembly().GetName().Version}");
        Debug.Assert(added);
    }

    public static async Task<bool> CheckAsync(Settings settings, ILogger logger, CancellationToken cancellationToken)
    {
        if (settings.SkipFileChecks is not true)
        {
            LogValidatingFiles(logger);
        }
        else
        {
            LogValidateFilesSkipped(logger);
            return true;
        }

        bool error = false;
        if (!EventBusServer.Check(settings, logger) ||
            !ObjectStoreServer.Check(settings, logger) ||
            !ApiServer.Check(settings, logger) ||
            !BuildplateLauncher.Check(settings, logger) ||
            !TappablesGenerator.Check(settings, logger) ||
            !TileRenderer.Check(settings, logger))
        {
            error = true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (string dir in expectedStaticDirectories)
        {
            string fullDir = Path.GetFullPath(Path.Combine(Program.StaticDataDir, dir));

            if (!Directory.Exists(fullDir))
            {
                Directory.CreateDirectory(fullDir);
                LogStaticDataNotFoundCreated(logger, fullDir);
            }
        }

        foreach (string file in expectedStaticFiles)
        {
            string fullFile = Path.GetFullPath(Path.Combine(Program.StaticDataDir, file));

            if (!File.Exists(fullFile))
            {
                LogStaticDataNotFoundError(logger, fullFile);
                error = true;
            }
        }

        if (!error)
        {
            LogStaticFilesOk(logger);
        }
        else
        {
            LogStaticFilesMissing(logger);
        }

        var resourcePack = new FileInfo(Path.GetFullPath(Path.Combine(Program.StaticDataDir, "resourcepacks", "vanilla.zip")));
        if (!resourcePack.Exists)
        {
            LogResourcepackFileNotFound(logger, resourcePack.FullName);
            LogResourcepackFileDownloadInstructions1(logger);
#pragma warning disable CA1873 // Avoid potentially expensive logging
            LogResourcepackFileDownloadInstructions2(logger, Path.GetFullPath(Path.Combine(Program.StaticDataDir, "resourcepacks")));
#pragma warning restore CA1873 // Avoid potentially expensive logging

            error = true;
        }
        else if (resourcePack.Length < 100_000_000)
        {
            LogResourcepackFileInvalid(logger, resourcePack.FullName, resourcePack.Length);
            LogResourcepackFileDownloadInstructions1(logger);
#pragma warning disable CA1873 // Avoid potentially expensive logging
            LogResourcepackFileDownloadInstructions2(logger, Path.GetFullPath(Path.Combine(Program.StaticDataDir, "resourcepacks")));
#pragma warning restore CA1873 // Avoid potentially expensive logging

            error = true;
        }

        if (!Directory.EnumerateFiles(Path.Combine(Program.StaticDataDir, "server_template_dir", "mods")).Any(path => Path.GetFileName(path).StartsWith("fabric-api", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            LogFabricApiModNotFound(logger);

            var response = await httpClient.GetAsync("https://cdn.modrinth.com/data/P7dR8mSH/versions/xklQBMta/fabric-api-0.97.0%2B1.20.4.jar", cancellationToken);
            using (var fs = File.OpenWriteNew(Path.Combine(Program.StaticDataDir, "server_template_dir", "mods", "fabric-api-0.97.0+1.20.4.jar")))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            LogDownloadedFabricApi(logger);
        }

        if (!File.Exists(Path.Combine(Program.StaticDataDir, "server_template_dir", BuildplateLauncher.ServerJarName)))
        {
            LogFabricServerNotFound(logger);

            var response = await httpClient.GetAsync("https://meta.fabricmc.net/v2/versions/loader/1.20.4/0.15.10/1.0.1/server/jar", cancellationToken);
            using (var fs = File.OpenWriteNew(Path.Combine(Program.StaticDataDir, "server_template_dir", BuildplateLauncher.ServerJarName)))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            LogDownloadedFabricServer(logger);
        }

        string eulaPath = Path.GetFullPath(Path.Combine(Program.StaticDataDir, "server_template_dir", "eula.txt"));
        if (!File.Exists(eulaPath))
        {
            LogServerNotSetup(logger);

            string javaExe = JavaLocator.Locate(logger);

            bool useShellExecute = false;

            using var serverProcess = new ConsoleProcess(javaExe, logger, useShellExecute, !useShellExecute);

            if (!useShellExecute)
            {
                serverProcess.StandartTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        LogReceiveServerMessage(logger, LogLevel.Debug, e.Data);
                    }
                };
                serverProcess.ErrorTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        LogReceiveServerMessage(logger, LogLevel.Error, e.Data);
                    }
                };
            }

            await serverProcess.ExecuteAsync(Path.GetFullPath(Path.Combine(Program.StaticDataDir, "server_template_dir")), ["-jar", BuildplateLauncher.ServerJarName, "-nogui"]);
            LogWaitingForServerExit(logger);
            await serverProcess.Process.WaitForExitAsync(cancellationToken); // todo: timeout?

            var exitCode = serverProcess.Process.ExitCode;
            LogServerProcessDone(logger, exitCode);
            if (exitCode != 0)
            {
                error = true;
            }
        }

        if (File.Exists(eulaPath) && !(await File.ReadAllTextAsync(eulaPath, cancellationToken)).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
        {
            LogEulaNotAccepted(logger, eulaPath);
            LogWaitingForEula(logger);
            while (!(await File.ReadAllTextAsync(eulaPath, cancellationToken)).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(1000, cancellationToken);
            }

            LogRunningServerToSetupFiles(logger);

            string javaExe = JavaLocator.Locate(logger);

            bool useShellExecute = true;

            using var serverProcess = new ConsoleProcess(javaExe, logger, useShellExecute, !useShellExecute);

            if (!useShellExecute)
            {
                serverProcess.StandartTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        LogReceiveServerMessage(logger, LogLevel.Debug, e.Data);
                    }
                };
                serverProcess.ErrorTextReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        LogReceiveServerMessage(logger, LogLevel.Error, e.Data);
                    }
                };
            }

            await serverProcess.ExecuteAsync(Path.GetFullPath(Path.Combine(Program.StaticDataDir, "server_template_dir")), ["-jar", BuildplateLauncher.ServerJarName, "-nogui"]);
            LogWaitingForServerExit(logger);
            await serverProcess.Process.WaitForExitAsync(cancellationToken); // todo: timeout?

            var exitCode = serverProcess.Process.ExitCode;
            LogServerProcessDone(logger, exitCode);
            if (exitCode != 0)
            {
                error = true;
            }
        }

        return !error;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Validating files")]
    private static partial void LogValidatingFiles(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped file validation, you can turn it back on in 'Configure/Skip file validation before starting'")]
    private static partial void LogValidateFilesSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Static data directory '{Path}' did not exist, created")]
    private static partial void LogStaticDataNotFoundCreated(ILogger logger, string Path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Static data file '{Path}' does not exist")]
    private static partial void LogStaticDataNotFoundError(ILogger logger, string Path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "All static files exist")]
    private static partial void LogStaticFilesOk(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Some static files missing")]
    private static partial void LogStaticFilesMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resourcepack file '{Path}' does not exist")]
    private static partial void LogResourcepackFileNotFound(ILogger logger, string Path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resourcepack file '{Path}' is invalid, expected size: 131885348B, actual size: {Size}B")]
    private static partial void LogResourcepackFileInvalid(ILogger logger, string Path, long Size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download it from https://cdn.mceserv.net/availableresourcepack/resourcepacks/dba38e59-091a-4826-b76a-a08d7de5a9e2-1301b0c257a311678123b9e7325d0d6c61db3c35 (using internet archive)")]
    private static partial void LogResourcepackFileDownloadInstructions1(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rename it to vanilla.zip and move it to: {Path}")]
    private static partial void LogResourcepackFileDownloadInstructions2(ILogger logger, string Path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fabric api mod not found, downloading")]
    private static partial void LogFabricApiModNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded fabric api")]
    private static partial void LogDownloadedFabricApi(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fabric server not found, downloading")]
    private static partial void LogFabricServerNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded fabric server")]
    private static partial void LogDownloadedFabricServer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected that server was not setup, running")]
    private static partial void LogServerNotSetup(ILogger logger);

    [LoggerMessage(Message = "[server] {Message}")]
    private static partial void LogReceiveServerMessage(ILogger logger, LogLevel logLevel, string Message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server process started, waiting for exit")]
    private static partial void LogWaitingForServerExit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server process exited with exit code {ExitCode}")]
    private static partial void LogServerProcessDone(ILogger logger, int ExitCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server eula not accepted, open '{Path}' and set 'eula=true'")]
    private static partial void LogEulaNotAccepted(ILogger logger, string Path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for you to make the change")]
    private static partial void LogWaitingForEula(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running server to download/generate rest of the files, close it after it starts up")]
    private static partial void LogRunningServerToSetupFiles(ILogger logger);
}
