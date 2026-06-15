using Microsoft.Extensions.Logging;

namespace Solace.ObjectStore.Server;

internal sealed partial class Server
{
    private readonly DataStore _dataStore;

    private readonly ILogger<Server> _logger;

    public Server(DataStore dataStore, ILogger<Server> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public async Task<string?> StoreAsync(byte[] data)
    {
        try
        {
            string id = await _dataStore.StoreAsync(data);
            LogStoredNewObject(id);
            return id;
        }
        catch (DataStore.DataStoreException exception)
        {
            LogFailedToStoreObject(exception);
            return null;
        }
    }

    public async Task<byte[]?> LoadAsync(string id)
    {
        LogRequestForObject(id);

        try
        {
            byte[]? data = await _dataStore.LoadAsync(id);
            if (data is null)
            {
                LogRequestedObjectDoesNotExist(id);
            }

            return data;
        }
        catch (DataStore.DataStoreException exception)
        {
            LogFailedToLoadObject(exception, id);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        LogRequestToDeleteObject(id);
        await _dataStore.DeleteAsync(id);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored new object '{Id}'")]
    private partial void LogStoredNewObject(string Id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to store object")]
    private partial void LogFailedToStoreObject(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request for object '{Id}'")]
    private partial void LogRequestForObject(string Id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Requested object '{Id}' does not exist")]
    private partial void LogRequestedObjectDoesNotExist(string Id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load object '{Id}'")]
    private partial void LogFailedToLoadObject(Exception exception, string Id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request to delete object '{Id}'")]
    private partial void LogRequestToDeleteObject(string Id);
}
