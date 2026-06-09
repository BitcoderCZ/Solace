using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Solace.Buildplate.Connector.Model;
using Solace.Buildplate.Model;
using Solace.Common;
using Solace.Common.Utils;
using Solace.EventBus.Client;

namespace Solace.Buildplate.Launcher;

public sealed partial class InstanceManager
{
    private readonly Starter _starter;

    private readonly Publisher _publisher;
    private RequestHandler _requestHandler = null!;
    private int _runningInstanceCount;
    private bool _shuttingDown;

    private readonly ILogger _logger;

    private readonly Lock _lock = new Lock();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    private enum InstanceType
    {
        BUILD,
        PLAY,
        SHARED_BUILD,
        SHARED_PLAY,
        ENCOUNTER,
    }

    private sealed record StartRequest(
        Guid? PlayerId,
        Guid? EncounterId,
        Guid BuildplateId,
        bool Night,
        InstanceType Type,
        long ShutdownTime
    );

    private sealed record StartNotification(
        Guid InstanceId,
        Guid? PlayerId,
        Guid? EncounterId,
        Guid BuildplateId,
        string Address,
        int Port,
        InstanceType Type
    );

    public InstanceManager(Starter starter, Publisher publisher, ILogger logger)
    {
        _starter = starter;

        _publisher = publisher;
        _logger = logger;
    }

    public static async Task<InstanceManager> CreateAsync(EventBusClient eventBusClient, Starter starter, ILogger logger)
    {
        var publisher = await eventBusClient.AddPublisherAsync();

        var instanceManager = new InstanceManager(starter, publisher, logger);

        instanceManager._requestHandler = await eventBusClient.AddRequestHandlerAsync("buildplates", new RequestHandlerLister(
            async request =>
            {
                if (request.Type is "start")
                {
                    instanceManager._lock.Enter();
                    if (instanceManager._shuttingDown)
                    {
                        instanceManager._lock.Exit();
                        return null;
                    }

                    instanceManager._runningInstanceCount += 1;
                    instanceManager._lock.Exit();

                    StartRequest startRequest;
                    try
                    {
                        startRequest = Json.Deserialize<StartRequest>(request.Data)!;
                    }
                    catch (Exception exception)
                    {
                        LogBadStartRequest(logger, exception);
                        return null;
                    }

                    bool survival;
                    bool saveEnabled;
                    InventoryType inventoryType;
                    Instance.BuildplateSource buildplateSource;
                    long? shutdownTime;
                    switch (startRequest.Type)
                    {
                        case InstanceType.BUILD:
                            {
                                survival = false;
                                saveEnabled = true;
                                inventoryType = InventoryType.SYNCED;
                                buildplateSource = Instance.BuildplateSource.PLAYER;
                                shutdownTime = null;
                            }

                            break;
                        case InstanceType.PLAY:
                            {
                                survival = true;
                                saveEnabled = false;
                                inventoryType = InventoryType.DISCARD;
                                buildplateSource = Instance.BuildplateSource.PLAYER;
                                shutdownTime = null;
                            }

                            break;
                        case InstanceType.SHARED_BUILD:
                            {
                                survival = false;
                                saveEnabled = false;
                                inventoryType = InventoryType.DISCARD;
                                buildplateSource = Instance.BuildplateSource.SHARED;
                                shutdownTime = null;
                            }

                            break;
                        case InstanceType.SHARED_PLAY:
                            {
                                survival = true;
                                saveEnabled = false;
                                inventoryType = InventoryType.DISCARD;
                                buildplateSource = Instance.BuildplateSource.SHARED;
                                shutdownTime = null;
                            }

                            break;
                        case InstanceType.ENCOUNTER:
                            {
                                survival = true;
                                saveEnabled = false;
                                inventoryType = InventoryType.BACKPACK;
                                buildplateSource = Instance.BuildplateSource.ENCOUNTER;
                                shutdownTime = startRequest.ShutdownTime;
                            }

                            break;
                        default:
                            {
                                LogBadStartRequest(logger);
                                return null;
                            }
                    }

                    if (buildplateSource == Instance.BuildplateSource.PLAYER && startRequest.PlayerId is null)
                    {
                        LogBadStartRequest(logger);
                        return null;
                    }

                    var instanceId = Guid.CreateVersion7();

                    LogStartingBuildplateInstance(logger, instanceId);

                    var instance = instanceManager._starter.StartInstance(instanceId, startRequest.PlayerId, startRequest.BuildplateId, buildplateSource, survival, startRequest.Night, saveEnabled, inventoryType, shutdownTime);
                    if (instance is null)
                    {
                        LogErrorStartingBuildplateInstance(logger, instanceId);
                        return null;
                    }

                    instanceManager.SendEventBusMessage("started", Json.Serialize(new StartNotification(
                        instanceId,
                        startRequest.PlayerId,
                        startRequest.EncounterId,
                        startRequest.BuildplateId,
                        instance.PublicAddress,
                        instance.Port,
                        startRequest.Type
                    )));

                    Task.Run(async () =>
                    {
                        try
                        {
                            await instance.WaitForShutdownAsync();

                            instanceManager.SendEventBusMessage("stopped", instance.InstanceId.ToString());
                        }
                        catch (Exception exception)
                        {
                            LogFailedToSendStoppedMessage(logger, exception);
                        }

                        instanceManager._lock.Enter();
                        instanceManager._runningInstanceCount -= 1;
                        instanceManager._lock.Exit();
                    }).Forget();

                    return instanceId.ToString();
                }
                else if (request.Type is "preview")
                {
                    PreviewRequest previewRequest;
                    byte[] serverData;
                    try
                    {
                        previewRequest = Json.Deserialize<PreviewRequest>(request.Data)!;
                        serverData = Convert.FromBase64String(previewRequest.ServerDataBase64);
                    }
                    catch (Exception exception)
                    {
                        LogBadPreviewRequest(logger, exception);
                        return null;
                    }

                    LogGeneratingBuildplatePreview(logger);

                    string? preview = PreviewGenerator.GeneratePreview(serverData, previewRequest.Night, Program.StaticDataPath, logger);
                    if (preview is null)
                    {
                        LogCouldNotGeneratePreviewForBuildplate(logger);
                    }

                    return preview;
                }
                else
                {
                    return null;
                }
            },
            async () =>
            {
                LogEventBusRequestHandlerError(logger);
            }
        ));

        return instanceManager;
    }

    private void SendEventBusMessage(string type, string message)
        => _publisher.PublishAsync("buildplates", type, message).ContinueWith(task =>
        {
            if (!task.Result)
            {
                LogEventBusPublisherError();
            }
        });

    public async Task ShutdownAsync()
    {
        await _requestHandler.CloseAsync();

        _lock.Enter();
        _shuttingDown = true;
        LogShutdownSignalReceived(_runningInstanceCount);
        while (_runningInstanceCount > 0)
        {
            int runningInstanceCount = _runningInstanceCount;
            _lock.Exit();

            await Task.Delay(1000);

            _lock.Enter();
            if (_runningInstanceCount != runningInstanceCount)
            {
                LogWaitingForInstancesToFinish(runningInstanceCount);
            }
        }

        _lock.Exit();

        await _publisher.FlushAsync();
        await _publisher.CloseAsync();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Bad start request")]
    private static partial void LogBadStartRequest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bad start request")]
    private static partial void LogBadStartRequest(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting buildplate instance '{InstanceId}'")]
    private static partial void LogStartingBuildplateInstance(ILogger logger, Guid InstanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error starting buildplate instance '{InstanceId}'")]
    private static partial void LogErrorStartingBuildplateInstance(ILogger logger, Guid InstanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send stopped message")]
    private static partial void LogFailedToSendStoppedMessage(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bad preview request")]
    private static partial void LogBadPreviewRequest(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating buildplate preview")]
    private static partial void LogGeneratingBuildplatePreview(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not generate preview for buildplate")]
    private static partial void LogCouldNotGeneratePreviewForBuildplate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event bus request handler error")]
    private static partial void LogEventBusRequestHandlerError(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event bus publisher error")]
    private partial void LogEventBusPublisherError();

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown signal received, no new buildplate instances will be started, waiting for {RunningInstanceCount} instances to finish")]
    private partial void LogShutdownSignalReceived(int RunningInstanceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for {RunningInstanceCount} instances to finish")]
    private partial void LogWaitingForInstancesToFinish(int RunningInstanceCount);
}