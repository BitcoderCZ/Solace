using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Solace.Launcher;

if (!Debugger.IsAttached)
{
    AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
    {
        Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");

        Console.Out.Flush();
        Console.Error.Flush();

        Environment.Exit(1);
    };
}

const string InstallationVersionPath = "../../version.txt";
const string InstallationSettingsPath = "../../settings.json";

try
{
    if (File.Exists(InstallationVersionPath) && File.Exists(InstallationSettingsPath))
    {
        var actualVersion = File.ReadAllText(InstallationVersionPath).Trim();

        var settingsJson = File.ReadAllText(InstallationSettingsPath);
        var settingsNode = JsonNode.Parse(settingsJson);

        if (settingsNode is JsonObject settingsObject)
        {
            var currentSettingsVersion = settingsObject["version"]?.ToString();

            if (currentSettingsVersion != actualVersion)
            {
                Console.WriteLine($"Version mismatch detected ({currentSettingsVersion} -> {actualVersion}). Updating...");

                settingsObject["version"] = actualVersion;
                settingsObject["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                File.WriteAllText(InstallationSettingsPath, settingsObject.ToJsonString(options));

                Console.WriteLine("Settings updated successfully.");
            }
            else
            {
                Console.WriteLine("Version is up to date.");
            }
        }
    }
}
catch (JsonException ex)
{
    Console.WriteLine($"JSON parsing error: {ex.Message}");
}
catch (IOException ex)
{
    Console.WriteLine($"File I/O error: {ex.Message}");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<Starter>();
builder.Services.AddSingleton<Updater>();
builder.Services.AddSingleton<UIManager>();

using var host = builder.Build();

var uiManager = host.Services.GetRequiredService<UIManager>();

await uiManager.RunAsync();