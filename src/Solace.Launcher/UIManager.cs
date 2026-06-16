using Spectre.Console;

namespace Solace.Launcher;

internal sealed class UIManager
{
    private readonly Starter _starter;

    public UIManager(Starter starter)
    {
        _starter = starter;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            var choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("Solace")
                    .AddChoices("Start", "Stop", "Status", "Exit")
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

                    foreach (var (name, online) in _starter.ComponentStatus)
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