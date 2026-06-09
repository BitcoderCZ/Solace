using Microsoft.Extensions.Logging;
using Solace.Common;
using Solace.Common.Utils;
using Solace.EventBus.Client;

namespace Solace.TappablesGenerator;

public sealed partial class ActiveTiles
{
    private static readonly int ACTIVE_TILE_RADIUS = 3;
    private static readonly long ACTIVE_TILE_EXPIRY_TIME = 2 * 60 * 1000;

    private readonly Dictionary<int, ActiveTile> _activeTiles = [];
    private readonly IActiveTileListener _activeTileListener;

    private readonly ILogger _logger;

    private ActiveTiles(IActiveTileListener activeTileListener, ILogger logger)
    {
        _activeTileListener = activeTileListener;
        _logger = logger;
    }

    public static async Task<ActiveTiles> CreateAsync(EventBusClient eventBusClient, IActiveTileListener activeTileListener, ILogger logger)
    {
        var tiles = new ActiveTiles(activeTileListener, logger);

        await eventBusClient.AddRequestHandlerAsync("tappables", new RequestHandlerLister(async request =>
        {
            if (request.Type == "activeTile")
            {
                ActiveTileNotification activeTileNotification;
                try
                {
                    activeTileNotification = Json.Deserialize<ActiveTileNotification>(request.Data)!;
                }
                catch (Exception exception)
                {
                    LogCouldNotDeserialiseActiveTileNotificationEvent(logger, exception);
                    return null;
                }

                long currentTime = U.CurrentTimeMillis();
                tiles.PruneActiveTiles(currentTime);

                int sideLength = (ACTIVE_TILE_RADIUS * 2) + 1;
                var newActiveTiles = new List<ActiveTile>(sideLength * sideLength);
                for (int tileX = activeTileNotification.X - ACTIVE_TILE_RADIUS; tileX < activeTileNotification.X + ACTIVE_TILE_RADIUS + 1; tileX++)
                {
                    for (int tileY = activeTileNotification.Y - ACTIVE_TILE_RADIUS; tileY < activeTileNotification.Y + ACTIVE_TILE_RADIUS + 1; tileY++)
                    {
                        ActiveTile activeTile = tiles.MarkTileActive(tileX, tileY, currentTime);

                        if (activeTile.LatestActiveTime == activeTile.FirstActiveTime) // indicating that the tile is newly-active
                        {
                            newActiveTiles.Add(activeTile);
                        }
                    }
                }

                if (newActiveTiles.Count > 0)
                {
                    await activeTileListener.Active(newActiveTiles);
                }

                return string.Empty;
            }
            else
            {
                return null;
            }
        },
        async () =>
        {
            LogEventBusSubscriberError(logger);
            Serilog.Log.CloseAndFlush();
            Environment.Exit(1);
        }));

        return tiles;
    }

    public IEnumerable<ActiveTile> GetActiveTiles(long currentTime)
        => _activeTiles.Values.Where(activeTile => currentTime < activeTile.LatestActiveTime + ACTIVE_TILE_EXPIRY_TIME);

    private ActiveTile MarkTileActive(int tileX, int tileY, long currentTime)
    {
        var activeTile = _activeTiles.GetValueOrDefault((tileX << 16) + tileY);
        if (activeTile is null)
        {
            LogTileIsBecomingActive(tileX, tileY);
            activeTile = new ActiveTile(tileX, tileY, currentTime, currentTime);
        }
        else
        {
            activeTile = new ActiveTile(tileX, tileY, activeTile.FirstActiveTime, currentTime);
        }

        _activeTiles[(tileX << 16) + tileY] = activeTile;

        return activeTile;
    }

    private void PruneActiveTiles(long currentTime)
    {
        List<KeyValuePair<int, ActiveTile>> entriesToRemove = [];

        foreach (var item in _activeTiles)
        {
            ActiveTile activeTile = item.Value;
            if (activeTile.LatestActiveTime + ACTIVE_TILE_EXPIRY_TIME <= currentTime)
            {
                LogTileIsInactive(activeTile.TileX, activeTile.TileY);
                entriesToRemove.Add(item);
            }
        }

        foreach (var item in entriesToRemove)
        {
            _activeTiles.Remove(item.Key);
        }

        _activeTileListener.Inactive(entriesToRemove.Select(item => item.Value));
    }

    public record ActiveTile(
        int TileX,
        int TileY,
        long FirstActiveTime,
        long LatestActiveTime
    );

    private sealed record ActiveTileNotification(
        int X,
        int Y,
        string PlayerId
    );

    public interface IActiveTileListener
    {
        Task Active(IEnumerable<ActiveTile> activeTiles);

        Task Inactive(IEnumerable<ActiveTile> activeTiles);
    }

    public class ActiveTileListener : IActiveTileListener
    {
        public Func<IEnumerable<ActiveTile>, Task>? OnActive;
        public Func<IEnumerable<ActiveTile>, Task>? OnInactive;

        public ActiveTileListener(Func<IEnumerable<ActiveTile>, Task>? active, Func<IEnumerable<ActiveTile>, Task>? inactive)
        {
            OnActive = active;
            OnInactive = inactive;
        }

        public Task Active(IEnumerable<ActiveTile> activeTiles)
            => OnActive?.Invoke(activeTiles) ?? Task.CompletedTask;

        public Task Inactive(IEnumerable<ActiveTile> activeTiles)
            => OnInactive?.Invoke(activeTiles) ?? Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not deserialise active tile notification event")]
    private static partial void LogCouldNotDeserialiseActiveTileNotificationEvent(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event bus subscriber error")]
    private static partial void LogEventBusSubscriberError(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tile ({PosX}, {PosY}) is becoming active")]
    private partial void LogTileIsBecomingActive(int PosX, int PosY);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tile ({PosX}, {PosY}) is inactive")]
    private partial void LogTileIsInactive(int PosX, int PosY);
}

