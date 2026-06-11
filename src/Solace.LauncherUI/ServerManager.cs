using System.Diagnostics;
using Solace.Common.Utils;
using Solace.LauncherUI.Programs;
using Solace.LauncherUI.Utils;

namespace Solace.LauncherUI;

public sealed class ServerComponent
{
    public string Name { get; }
    public string ExeName { get; }
    public Func<Settings, ILogger, Process?> StartAction { get; }
    public int StartupDelayMs { get; }
    public Func<Settings, bool> IsEnabled { get; }

    public ServerStatus Status { get; set; } = ServerStatus.Offline;

    public ServerComponent(string name, string exeName, Func<Settings, ILogger, Process?> startAction, int startupDelayMs = 0, Func<Settings, bool>? isEnabled = null)
    {
        Name = name;
        ExeName = exeName;
        StartAction = startAction;
        StartupDelayMs = startupDelayMs;
        IsEnabled = isEnabled ?? (_ => true);
    }
}

public sealed partial class ServerManager : IDisposable
{
    public event Action? OnStatusChanged;

    private ServerStatus _status = ServerStatus.Offline;
    public ServerStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnStatusChanged?.Invoke();
            }
        }
    }

    public bool AnyOnline { get; private set; }

    private int _startLockCount;
    public bool StartLocked => Volatile.Read(ref _startLockCount) > 0;

    public bool CanStart => !StartLocked && Status is not (ServerStatus.Starting or ServerStatus.Online);
    public bool CanRestart => Status is not (ServerStatus.Stopping or ServerStatus.Offline);
    public bool CanStop => Status is not (ServerStatus.Stopping or ServerStatus.Offline);

    public IReadOnlyList<ServerComponent> Components { get; }

    public IReadOnlyList<int> ComponentShutdownOrder { get; }

    private readonly Lock _statusLock = new Lock();

    private CancellationTokenSource? _operationTokenSource;

    private readonly ILogger<ServerManager> _logger;

    public ServerManager(ILogger<ServerManager> logger)
    {
        _logger = logger;

        Components =
        [
            new("Event Bus", EventBusServer.ExeName, EventBusServer.Run), // 0
            new("Object Store", ObjectStoreServer.ExeName, ObjectStoreServer.Run, 1000), // 1
            new("Buildplate Launcher", BuildplateLauncher.ExeName, BuildplateLauncher.Run, 1500), // 2
            new("API Server", ApiServer.ExeName, ApiServer.Run), // 3
            new("Tappables Generator", TappablesGenerator.ExeName, TappablesGenerator.Run), // 4
            new("Tile Renderer", TileRenderer.ExeName, TileRenderer.Run, 0, s => s.EnableTileRenderingLabel ?? true), // 5
        ];

        ComponentShutdownOrder =
        [
            5,
            4,
            2,
            3,
            1,
            0,
        ];

        RefreshComponentStatuses();
    }

    public void RefreshComponentStatuses(bool detectRunning = true, bool preserveCurrentStatus = false)
    {
        var settings = Settings.Instance;
        bool anyOnline = false;

        foreach (var comp in Components)
        {
            if (detectRunning)
            {
                bool isRunning = ProcessUtils.GetProgramProcesses(comp.ExeName).Any();
                comp.Status = comp.Status switch
                {
                    ServerStatus.Starting => isRunning ? ServerStatus.Online : ServerStatus.Starting,
                    ServerStatus.Stopping => isRunning ? ServerStatus.Stopping : ServerStatus.Offline,
                    _ => isRunning ? ServerStatus.Online : ServerStatus.Offline,
                };
            }

            if (comp.Status is ServerStatus.Online && comp.IsEnabled(settings))
            {
                anyOnline = true;
            }
        }

        AnyOnline = anyOnline;
        var newStatus = ComputeGlobalStatus();
        if (preserveCurrentStatus && Status is ServerStatus.Starting && newStatus is ServerStatus.Offline)
        {
            return;
        }

        if (preserveCurrentStatus && Status is ServerStatus.Stopping && newStatus is ServerStatus.Offline)
        {
            return;
        }

        Status = newStatus;
    }

    public IDisposable? AcquireStartLock()
    {
        lock (_statusLock)
        {
            if (Status is ServerStatus.Offline)
            {
                return null;
            }

            return new StartLockHandle(this);
        }
    }

    private ServerStatus ComputeGlobalStatus()
    {
        if (Components.Any(c => c.Status is ServerStatus.Stopping))
        {
            return ServerStatus.Stopping;
        }

        if (Components.Any(c => c.Status is ServerStatus.Starting))
        {
            return ServerStatus.Starting;
        }

        var enabledComponents = Components.Where(c => c.IsEnabled(Settings.Instance)).ToList();
        var onlineCount = enabledComponents.Count(c => c.Status is ServerStatus.Online);
        var offlineCount = enabledComponents.Count(c => c.Status is ServerStatus.Offline);

        if (enabledComponents.Count == 0)
        {
            return ServerStatus.Offline;
        }

        if (onlineCount > 0 && offlineCount > 0)
        {
            return ServerStatus.PartiallyOnline;
        }

        return onlineCount > 0 ? ServerStatus.Online : ServerStatus.Offline;
    }

    private static async Task<bool> WaitForProcessStartAsync(string exeName, CancellationToken cancellationToken)
    {
        const int intervalMs = 200;
        const int maxAttempts = 50;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ProcessUtils.GetProgramProcesses(exeName).Any())
            {
                return true;
            }

            await Task.Delay(intervalMs, cancellationToken);
        }

        return false;
    }

    public async Task<bool> EnsureComponentsOnline(params string[] exeNames)
    {
        if (exeNames is null || exeNames.Length == 0)
        {
            return true;
        }

        List<ServerComponent> targets;
        var reservedToStart = new HashSet<ServerComponent>();

        lock (_statusLock)
        {
            if (StartLocked)
            {
                LogEnsureComponentsOnlineBlockedStartLocked();
                return false;
            }

            // Identify which of our managed components match the requested EXEs
            targets = [.. Components.Where(c => exeNames.Contains(c.ExeName, StringComparer.OrdinalIgnoreCase))];

            if (targets.Count == 0)
            {
                return true;
            }

            if (Status is ServerStatus.Stopping)
            {
                return false;
            }

            foreach (var comp in targets)
            {
                if (comp.Status is ServerStatus.Offline)
                {
                    comp.Status = ServerStatus.Starting;
                    reservedToStart.Add(comp);
                }
            }

            if (targets.All(t => t.Status is ServerStatus.Online))
            {
                return true;
            }

            if (Status is ServerStatus.Offline or ServerStatus.PartiallyOnline)
            {
                Status = ServerStatus.Starting;
            }
        }

        var settings = Settings.Instance;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Safety timeout

        try
        {
            foreach (var comp in targets)
            {
                if (comp.Status is ServerStatus.Online)
                {
                    continue;
                }

                bool shouldStart = reservedToStart.Contains(comp);
                if (shouldStart)
                {
                    LogSelectiveStartupStartingComponent(comp.Name);
                }
                else
                {
                    LogSelectiveStartupWaitingForComponent(comp.Name);
                }

                var process = shouldStart ? comp.StartAction(settings, _logger) : null;
                if (comp.StartupDelayMs > 0)
                {
                    await Task.Delay(comp.StartupDelayMs, cts.Token);
                }

                if (await WaitForProcessStartAsync(comp.ExeName, cts.Token))
                {
                    comp.Status = ServerStatus.Online;
                }
                else
                {
                    comp.Status = ServerStatus.Offline;
                    if (process is not null && process.HasExited)
                    {
                        LogProcessExited(comp.Name);
                    }
                    else
                    {
                        LogProcessFailedToStart(comp.Name);
                    }
                }

                OnStatusChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // logger.Warning("Selective component startup cancelled.");
            return false;
        }
        catch (Exception exception)
        {
            LogSelectiveStartupFail(exception);
            return false;
        }
        finally
        {
            RefreshComponentStatuses();
        }

        return targets.All(t => t.Status is ServerStatus.Online);
    }

    public async Task Start(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if (!CanStart)
            {
                return;
            }

            cancellationToken = InitOperation(cancellationToken);
            Status = ServerStatus.Starting;
        }

        try
        {
            await StartInternal(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await Stop(default);
        }
    }

    public async Task Stop(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if ((Status is ServerStatus.Offline && !AnyOnline) || Status is ServerStatus.Stopping)
            {
                return;
            }

            cancellationToken = InitOperation(cancellationToken);
            Status = ServerStatus.Stopping;
        }

        foreach (var compIndex in ComponentShutdownOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var comp = Components[compIndex];

            if (comp.Status is ServerStatus.Offline)
            {
                continue;
            }

            comp.Status = ServerStatus.Stopping;
            OnStatusChanged?.Invoke();

            await StopProgram(comp.ExeName, _logger, cancellationToken);

            comp.Status = ServerStatus.Offline;
            OnStatusChanged?.Invoke();
        }

        cancellationToken.ThrowIfCancellationRequested();
        AnyOnline = false;
        Status = ServerStatus.Offline;
    }

    public async Task Restart(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            if (Status is ServerStatus.Stopping or ServerStatus.Offline)
            {
                return;
            }
        }

        await Stop(cancellationToken);

        lock (_statusLock)
        {
            if (StartLocked)
            {
                // Log.Logger.Warning("Restart aborted because server start is locked after stop.");
                return;
            }
        }

        await Start(cancellationToken);
    }

    public async Task KillAll(CancellationToken cancellationToken = default)
    {
        lock (_statusLock)
        {
            cancellationToken = InitOperation(cancellationToken);
            Status = ServerStatus.Stopping;
        }

        foreach (var compIndex in ComponentShutdownOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var comp = Components[compIndex];

            comp.Status = ServerStatus.Stopping;
            OnStatusChanged?.Invoke();

            await StopProgram(comp.ExeName, _logger, cancellationToken);

            comp.Status = ServerStatus.Offline;
            OnStatusChanged?.Invoke();
        }

        cancellationToken.ThrowIfCancellationRequested();
        AnyOnline = false;
        Status = ServerStatus.Offline;
    }

    public void Dispose()
        => _operationTokenSource?.Dispose();

    private async Task StartInternal(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = Settings.Instance;

        lock (_statusLock)
        {
            if (StartLocked)
            {
                // logger.Warning("Start prevented because server start is locked.");
                Status = ServerStatus.Offline;
                return;
            }
        }

        if (!await FileChecker.CheckAsync(settings, _logger, cancellationToken))
        {
            LogFileValidationFailed();
            Status = ServerStatus.Offline;
            return;
        }

        RefreshComponentStatuses(preserveCurrentStatus: true);

        foreach (var comp in Components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!comp.IsEnabled(settings))
            {
                comp.Status = ServerStatus.Offline;
                continue;
            }

            if (comp.Status is ServerStatus.Online)
            {
                LogComponentIsAlreadyRunning(comp.Name);
                continue;
            }

            comp.Status = ServerStatus.Starting;
            OnStatusChanged?.Invoke();

            var process = comp.StartAction(settings, _logger);
            if (comp.StartupDelayMs > 0)
            {
                await Task.Delay(comp.StartupDelayMs, cancellationToken);
            }

            if (await WaitForProcessStartAsync(comp.ExeName, cancellationToken))
            {
                comp.Status = ServerStatus.Online;
                AnyOnline = true;
            }
            else
            {
                comp.Status = ServerStatus.Offline;
                if (process is not null && process.HasExited)
                {
                    LogProcessExited(comp.Name);
                }
                else
                {
                    LogProcessFailedToStart(comp.Name);
                }
            }

            OnStatusChanged?.Invoke();
        }

        LogWaitingForProgramsToStabilize();
        await Task.Delay(7500, cancellationToken);

        bool error = false;
        foreach (var comp in Components)
        {
            if (!comp.IsEnabled(settings))
            {
                continue;
            }

            if (!ProcessUtils.GetProgramProcesses(comp.ExeName).Any())
            {
                LogProgramCrashed(comp.Name);
                comp.Status = ServerStatus.Offline;
                error = true;
            }
            else
            {
                comp.Status = ServerStatus.Online;
            }
        }

        RefreshComponentStatuses();

        if (!error)
        {
            LogAllProgramsStarted();
        }
    }

    private static async Task StopProgram(string name, ILogger logger, CancellationToken cancellationToken)
    {
        LogStoppingProgram(logger, name);

        int stoppedCount = 0;
        foreach (var process in ProcessUtils.GetProgramProcesses(name))
        {
            await process.StopGracefullyOrKillAndWaitAsync(3000, logger, cancellationToken);
            stoppedCount++;
        }

        switch (stoppedCount)
        {
            case 0:
                LogNoProcessesFound(logger, name);
                break;
            case 1:
                LogStoppedOneProcess(logger, name);
                break;
            default:
                LogStoppedXProcesses(logger, name, stoppedCount);
                break;
        }
    }

    private CancellationToken InitOperation(CancellationToken cancellationToken)
    {
        _operationTokenSource?.Cancel();
        _operationTokenSource = null;

        _operationTokenSource = new CancellationTokenSource();
        var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(_operationTokenSource.Token, cancellationToken);
        return combinedSource.Token;
    }

    private sealed class StartLockHandle : IDisposable
    {
        private readonly ServerManager _manager;
        private int _disposed;

        public StartLockHandle(ServerManager manager)
        {
            _manager = manager;
            if (Interlocked.Increment(ref _manager._startLockCount) == 1)
            {
                _manager.OnStatusChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var remaining = Interlocked.Decrement(ref _manager._startLockCount);
            if (remaining <= 0)
            {
                Interlocked.Exchange(ref _manager._startLockCount, 0);
                _manager.OnStatusChanged?.Invoke();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "EnsureComponentsOnline blocked because server start is locked")]
    private partial void LogEnsureComponentsOnlineBlockedStartLocked();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Selective startup: Starting {ComponentName}")]
    private partial void LogSelectiveStartupStartingComponent(string ComponentName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Selective startup: Waiting for {ComponentName} to become online")]
    private partial void LogSelectiveStartupWaitingForComponent(string ComponentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{ComponentName} process exited immediately after launch")]
    private partial void LogProcessExited(string ComponentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{ComponentName} failed to start")]
    private partial void LogProcessFailedToStart(string ComponentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during selective component startup")]
    private partial void LogSelectiveStartupFail(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "File validation failed")]
    private partial void LogFileValidationFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "{ComponentName} is already running")]
    private partial void LogComponentIsAlreadyRunning(string ComponentName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for programs to stabilize")]
    private partial void LogWaitingForProgramsToStabilize();

    [LoggerMessage(Level = LogLevel.Error, Message = "It was detected that {ComponentName} crashed/exited, make sure all options are set correctly, look into logs/{ComponentName}/logxxx for more info")]
    private partial void LogProgramCrashed(string ComponentName);

    [LoggerMessage(Level = LogLevel.Information, Message = "All required programs have (most likely) running successfully")]
    private partial void LogAllProgramsStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping {ComponentName}")]
    private static partial void LogStoppingProgram(ILogger logger, string ComponentName);

    [LoggerMessage(Level = LogLevel.Information, Message = "No {ComponentName} processes found")]
    private static partial void LogNoProcessesFound(ILogger logger, string ComponentName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped 1 {ComponentName} process")]
    private static partial void LogStoppedOneProcess(ILogger logger, string ComponentName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped {StoppedCount} {ComponentName} processes")]
    private static partial void LogStoppedXProcesses(ILogger logger, string ComponentName, int StoppedCount);
}

public enum ServerStatus
{
    Online = 0,
    Starting,
    Stopping,
    PartiallyOnline,
    Offline,
}