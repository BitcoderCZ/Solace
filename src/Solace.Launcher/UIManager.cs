using System.Reflection;
using Spectre.Console;

namespace Solace.Launcher;

internal sealed class UIManager
{
    private readonly Starter _starter;
    private readonly Updater _updater;

    public UIManager(Starter starter, Updater updater)
    {
        _starter = starter;
        _updater = updater;
    }

    public async Task RunAsync()
    {
        AnsiConsole.MarkupLine("""
            [blue]
                 _____       __
                / ___/____  / /___ _________
                \__ \/ __ \/ / __ \/ ___/ _ \
               ___/ / /_/ / / /_/ / /__/  __/
              /____/\____/_/\__,_/\___/\___/
            [/]
            """);

        AnsiConsole.WriteLine($"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    // .Title("Select option")
                    .AddChoices("Start", "Stop", "Status", "Update", "Exit")
            );

            AnsiConsole.MarkupLine($"Selected: [yellow]{choice}[/]");

            switch (choice)
            {
                case "Start":
                    await _starter.StartAsync();
                    break;
                case "Stop":
                    await _starter.StopAsync();
                    break;
                case "Status":
                    await ShowStatus();
                    break;
                case "Update":
                    var updateSuccessful = await _updater.UpdateAsync();
                    if (updateSuccessful)
                    {
                        return; // restart needed
                    }

                    break;
                case "Exit":
                    return;
            }
        }
    }

    private async Task ShowStatus()
    {
        var table = new Table().Expand().Border(TableBorder.Rounded);
        table.AddColumn("Component");
        table.AddColumn("Status");

        AnsiConsole.Live(table)
            .AutoClear(false)
            .Start(ctx =>
            {
                var random = new Random();

                while (!Console.KeyAvailable)
                {
                    table.Rows.Clear();

                    foreach (var (name, online) in _starter.GetComponentStatus())
                    {
                        table.AddRow(
                            name,
                            $"[{(online ? "green" : "red")}]{(online ? "Running" : "Stopped")}[/]"
                        );
                    }

                    table.Caption("[grey]Press any key to go back[/]");

                    ctx.Refresh();

                    Thread.Sleep(500);
                }

                Console.ReadKey(intercept: true);
            });
    }
}