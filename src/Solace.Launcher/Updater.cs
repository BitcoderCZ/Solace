using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Solace.Launcher.Utils;
using Spectre.Console;

namespace Solace.Launcher;

internal sealed partial class Updater
{
    private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions()
    {
        WriteIndented = true
    };

    private readonly Starter _starter;

    public Updater(Starter starter)
    {
        _starter = starter;
    }

    public async Task<bool> UpdateAsync()
    {
        if (_starter.GetComponentStatus().Any(item => item.Value))
        {
            AnsiConsole.MarkupLine("[red]Error: The server must be offline to update[/]");
            PressAnyKeyToContinue();
            return false;
        }

        var installationSettingsPath = Path.GetFullPath("../../settings.json");

        string? currentVersionString = null;
        Version? currentVersion = null;
        if (File.Exists(installationSettingsPath))
        {
            JsonObject? settings;
            using (var fs = File.OpenRead(installationSettingsPath))
            {
                settings = await JsonSerializer.DeserializeAsync<JsonObject>(fs);
            }

            if (settings is not null)
            {
                if (settings.TryGetPropertyValue("installMode", out var installModeNode) && installModeNode is JsonValue installMode && installMode.GetValue<string>() is not "prebuilt")
                {
                    if (!AnsiConsole.Confirm($"[yellow]Warning: Invalid install mode '{installMode.GetValue<string>()}', continue update?[/]", defaultValue: false))
                    {
                        return false;
                    }
                }

                if (settings.TryGetPropertyValue("version", out var versionNode) && versionNode is JsonValue version)
                {
                    var versionString = GetVersionCleanupRegex().Replace(version.GetValue<string>(), "");

                    if (Version.TryParse(versionString, out currentVersion))
                    {
                        currentVersionString = version.GetValue<string>();
                    }
                }
            }
        }

        const string owner = "BitcoderCZ";
        const string repo = "Solace";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SolaceUpdater/1.0)");

        GitHubRelease? release;
        try
        {
            release = await client.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{owner}/{repo}/releases/latest");

            if (release?.Assets is null)
            {
                AnsiConsole.MarkupLine("[red]Error: No assets found or failed to parse response.[/]");
                return false;
            }
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to get release[/]");
            AnsiConsole.WriteException(exception);
            PressAnyKeyToContinue();
            return false;
        }

        var newVersion = Version.Parse(GetVersionCleanupRegex().Replace(release.TagName, ""));
        if (currentVersion is not null && currentVersion >= newVersion)
        {
            AnsiConsole.MarkupLine("[yellow]Already up to date.[/]");
            PressAnyKeyToContinue();
            return false;
        }

        AnsiConsole.MarkupLine($"Updating... [yellow]{currentVersionString ?? "unknown"}[/] --> [green]{release.TagName}[/]");

        JsonObject? newDefaultSettings = null;
        try
        {
            string? settingsDownloadUrl = null;
            foreach (var asset in release.Assets)
            {
                if (string.Equals(asset.Name, "appsettings-Launcher.json", StringComparison.OrdinalIgnoreCase))
                {
                    settingsDownloadUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            if (string.IsNullOrEmpty(settingsDownloadUrl))
            {
                AnsiConsole.MarkupLine("[yellow]Warning: Default app settings not present in github release, current app settings will not be updated.[/]");
                PressAnyKeyToContinue();
            }
            else
            {
                using var response = await client.GetAsync(settingsDownloadUrl);
                newDefaultSettings = await response.Content.ReadFromJsonAsync<JsonObject>();
            }
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to download new default appsettings[/]");
            AnsiConsole.WriteException(exception);
            PressAnyKeyToContinue();
            return false;
        }

        var currentSettingsPath = new FileInfo("appsettings.json");
        var defaultSettingsPath = new FileInfo("DO_NOT_MODIFY_default_apps_settings.json");

        if (!currentSettingsPath.Exists)
        {
            if (defaultSettingsPath.Exists)
            {
                defaultSettingsPath.CopyTo(currentSettingsPath.FullName, overwrite: false);
                currentSettingsPath.Refresh();
            }
            else if (newDefaultSettings is not null)
            {
                using (var fs = currentSettingsPath.OpenWrite())
                {
                    await JsonSerializer.SerializeAsync(fs, newDefaultSettings, _serializerOptions);
                }

                using (var fs = defaultSettingsPath.OpenWrite())
                {
                    await JsonSerializer.SerializeAsync(fs, newDefaultSettings, _serializerOptions);
                }

                currentSettingsPath.Refresh();
                defaultSettingsPath.Refresh();
            }
        }

        var newSettingsPath = new FileInfo("../../appsettings.json.new");

        if (currentSettingsPath.Exists && defaultSettingsPath.Exists && newDefaultSettings is not null)
        {
            try
            {
                var currentSettings = JsonSerializer.Deserialize<JsonObject>(await File.ReadAllTextAsync(currentSettingsPath.FullName));
                var oldDefaultSettings = JsonSerializer.Deserialize<JsonObject>(await File.ReadAllTextAsync(defaultSettingsPath.FullName));
                var mergedSettings = JsonMigrator.MergeNodes(oldDefaultSettings, currentSettings, newDefaultSettings);

                await File.WriteAllTextAsync(newSettingsPath.FullName, JsonSerializer.Serialize(mergedSettings, _serializerOptions));

                AnsiConsole.MarkupLine("appsettings merged with new default");
            }
            catch (Exception exception)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to merge appsettings[/]");
                AnsiConsole.WriteException(exception);
                PressAnyKeyToContinue();
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Warning: Merging skipped, settings or default settings does not exist, or new default settings failed to download.[/]");
            PressAnyKeyToContinue();
        }

        var currentOsArch = GetOsArch();

        try
        {
            string? zipDownloadUrl = null;
            foreach (var asset in release.Assets)
            {
                if (string.Equals(asset.Name, $"Solace-{currentOsArch}.zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipDownloadUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            if (string.IsNullOrEmpty(zipDownloadUrl))
            {
                AnsiConsole.MarkupLine($"[red]Error: Could not get asset for current os/architecture '{currentOsArch}'.[/]");
                PressAnyKeyToContinue();
                return false;
            }
            else
            {
                var extractPath = Path.GetFullPath("../");
                AnsiConsole.MarkupLine($"Downloading and extracting release to [green]{Markup.Escape(extractPath)}[/]");

                await AnsiConsole.Progress()
                    .Columns(
                    [
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new RemainingTimeColumn(),
                        new SpinnerColumn(),
                    ])
                    .StartAsync(async ctx =>
                    {
                        var downloadTask = ctx.AddTask("Downloading release", autoStart: true, maxValue: 100);

                        using var response = await client.GetAsync(zipDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        using var downloadStream = await response.Content.ReadAsStreamAsync();
                        using var memoryStream = new MemoryStream();

                        if (totalBytes is -1)
                        {
                            downloadTask.IsIndeterminate(true);
                        }

                        var buffer = new byte[8192];
                        long totalReadBytes = 0;
                        int readBytes;

                        while ((readBytes = await downloadStream.ReadAsync(buffer)) > 0)
                        {
                            await memoryStream.WriteAsync(buffer.AsMemory(0, readBytes));
                            totalReadBytes += readBytes;

                            if (totalBytes != -1)
                            {
                                var percentage = (double)totalReadBytes / totalBytes * 100;
                                downloadTask.Value = percentage;
                            }
                        }

                        downloadTask.IsIndeterminate(false);
                        downloadTask.Value = 100;
                        downloadTask.StopTask();

                        var extractTask = ctx.AddTask("Extracting files", autoStart: true)
                            .IsIndeterminate(true);

                        memoryStream.Position = 0;
                        using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Read))
                        {
                            await Task.Run(() => zip.ExtractToDirectory(extractPath, overwriteFiles: true));
                        }

                        extractTask.IsIndeterminate(false);
                        extractTask.MaxValue = 100;
                        extractTask.Value = 100;
                        extractTask.StopTask();
                    });
            }
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to download new release[/]");
            AnsiConsole.WriteException(exception);
            PressAnyKeyToContinue();
            return false;
        }

        newSettingsPath.Refresh();
        if (newSettingsPath.Exists)
        {
            newSettingsPath.CopyTo(currentSettingsPath.FullName, overwrite: true);
            newSettingsPath.Delete();
        }

        // todo: update version in settings and version txt

        AnsiConsole.MarkupLine("[bold green]Update finished successfully![/]");
        AnsiConsole.WriteLine("Program restart is required");
        PressAnyKeyToContinue();

        return true;
    }

    private static void PressAnyKeyToContinue()
    {
        AnsiConsole.MarkupLine("Press any key to continue...");
        Console.ReadKey(true);
    }

    private static string GetOsArch()
    {
        string os = "unknown";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = "win";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = "osx";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            os = "linux";
        }

        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        return $"{os}-{arch}";
    }

    [GeneratedRegex("^v\\.?|-.+$")]
    private static partial Regex GetVersionCleanupRegex();

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public required string TagName { get; set; }

        [JsonPropertyName("assets")]
        public required GitHubAsset[] Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public required string BrowserDownloadUrl { get; set; }
    }
}