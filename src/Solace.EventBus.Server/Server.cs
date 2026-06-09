using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Solace.Common.Utils;

namespace Solace.EventBus.Server;

public sealed partial class Server : IDisposable
{
    private readonly ReaderWriterLockSlim _subscribersLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, HashSet<Subscriber>> _subscribers = [];

    private readonly ReaderWriterLockSlim _requestHandlersLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, HashSet<RequestHandler>> _requestHandlers = [];

    private readonly ILogger _logger;

    public Server(ILogger logger)
    {
        _logger = logger;
    }

    public Subscriber? AddSubscriber(string queueName, Action<Subscriber.Message> consumer)
    {
        if (!ValidateQueueName(queueName))
        {
            return null;
        }

        LogAddingSubscriber(queueName);

        _subscribersLock.EnterWriteLock();

        var subscriber = new Subscriber(this, queueName, consumer, _logger);
        _subscribers.ComputeIfAbsent(queueName, name => [])!.Add(subscriber);

        _subscribersLock.ExitWriteLock();

        return subscriber;
    }

    public sealed partial class Subscriber
    {
        private readonly Server _server;

        private readonly string _queueName;
        private readonly Action<Message> _consumer;
        private bool _ended;

        private readonly ILogger _logger;

        internal Subscriber(Server server, string queueName, Action<Message> consumer, ILogger logger)
        {
            _server = server;
            _queueName = queueName;
            _consumer = consumer;
            _logger = logger;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Remove()
        {
            _ended = true;

            Task.Run(() =>
            {
                LogRemovingSubscriber();

                _server._subscribersLock.EnterWriteLock();
                try
                {
                    if (_server._subscribers.TryGetValue(_queueName, out var subs))
                    {
                        subs.Remove(this);
                    }
                }
                finally
                {
                    _server._subscribersLock.ExitWriteLock();
                }
            });
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void Push(EntryMessage entryMessage)
        {
            if (!_ended)
            {
                _consumer.Invoke(entryMessage);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void Error()
        {
            if (!_ended)
            {
                _consumer.Invoke(new ErrorMessage());
                _ended = true;
            }
        }

        public abstract class Message
        {
            protected Message()
            {
                // empty
            }
        }

        public sealed class EntryMessage : Message
        {
            public readonly long Timestamp;
            public readonly string Type;
            public readonly string Data;

            internal EntryMessage(long timestamp, string type, string data)
            {
                Timestamp = timestamp;
                Type = type;
                Data = data;
            }
        }

        public sealed class ErrorMessage : Message
        {
            internal ErrorMessage()
            {
                // empty
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Removing subscriber")]
        private partial void LogRemovingSubscriber();
    }

    private HashSet<Subscriber> GetSubscribers(string queueName)
    {
        var subscribers = _subscribers.GetValueOrDefault(queueName);
        return subscribers is not null
            ? subscribers
            : [];
    }

    public Publisher AddPublisher()
    {
        LogAddingPublisher();
        return new Publisher(this, _logger);
    }

    public sealed partial class Publisher
    {
        private readonly Server _server;
        private bool _closed;

        private readonly ILogger _logger;

        public Publisher(Server server, ILogger logger)
        {
            _server = server;
            _logger = logger;
        }

        public void Remove()
        {
            LogRemovingPublisher();
            _closed = true;
        }

        public bool Publish(string queueName, long timestamp, string type, string data)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            if (!ValidateQueueName(queueName))
            {
                return false;
            }

            if (!ValidateType(type))
            {
                return false;
            }

            if (!ValidateData(data))
            {
                return false;
            }

            _server._subscribersLock.EnterReadLock();

            var message = new Subscriber.EntryMessage(timestamp, type, data);
            foreach (var subscriber in _server.GetSubscribers(queueName))
            {
                subscriber.Push(message);
            }

            _server._subscribersLock.ExitReadLock();

            return true;
        }
    
        [LoggerMessage(Level = LogLevel.Debug, Message = "Removing publisher")]
        private partial void LogRemovingPublisher();
    }

    public Server.RequestHandler? AddRequestHandler(string queueName, Func<RequestHandler.RequestR, TaskCompletionSource<string?>> requestHandler, Action<RequestHandler.ErrorMessage> errorConsumer)
    {
        if (!ValidateQueueName(queueName))
        {
            return null;
        }

        LogAddingRequestHandler(queueName);

        _requestHandlersLock.EnterWriteLock();

        var handler = new RequestHandler(this, queueName, requestHandler, errorConsumer, _logger);
        _requestHandlers.ComputeIfAbsent(queueName, name => [])!.Add(handler);

        _requestHandlersLock.ExitWriteLock();

        return handler;
    }

    public void Dispose()
    {
        _subscribersLock.Dispose();
        _requestHandlersLock.Dispose();
    }

    public sealed partial class RequestHandler
    {
        private readonly Server _server;

        private readonly string _queueName;
        private readonly Func<RequestR, TaskCompletionSource<string?>> _requestHandler;
        private readonly Action<ErrorMessage> _errorConsumer;
        private bool _ended;

        private readonly ILogger _logger;

        internal RequestHandler(Server server, string queueName, Func<RequestR, TaskCompletionSource<string?>> requestHandler, Action<ErrorMessage> errorConsumer, ILogger logger)
        {
            _server = server;
            _queueName = queueName;
            _requestHandler = requestHandler;
            _errorConsumer = errorConsumer;
            _logger = logger;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Remove()
        {
            _ended = true;

            Task.Run(() =>
            {
                LogRemovingHandler();

                _server._requestHandlersLock.EnterWriteLock();
                try
                {
                    if (_server._requestHandlers.TryGetValue(_queueName, out var handlers))
                    {
                        handlers.Remove(this);
                    }
                }
                finally
                {
                    _server._requestHandlersLock.ExitWriteLock();
                }
            });
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal TaskCompletionSource<string?> Request(RequestR request)
        {
            if (!_ended)
            {
                return _requestHandler.Invoke(request);
            }
            else
            {
                var source = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                source.SetResult(null);
                return source;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void Error()
        {
            if (!_ended)
            {
                _errorConsumer.Invoke(new ErrorMessage());
                _ended = true;
            }
        }

        public sealed class RequestR
        {
            public readonly long Timestamp;
            public readonly string Type;
            public readonly string Data;

            internal RequestR(long timestamp, string type, string data)
            {
                Timestamp = timestamp;
                Type = type;
                Data = data;
            }
        }

        public sealed class ErrorMessage
        {
            internal ErrorMessage()
            {
                // empty
            }
        }
    
        [LoggerMessage(Level = LogLevel.Debug, Message = "Removing handler")]
        private partial void LogRemovingHandler();
    }

    private HashSet<RequestHandler> GetHandlers(string queueName)
    {
        var requestHandlers = _requestHandlers.GetValueOrDefault(queueName);
        return requestHandlers is not null
            ? requestHandlers
            : [];
    }

    public RequestSender AddRequestSender()
    {
        LogAddingRequestSender();
        return new RequestSender(this, _logger);
    }

    public sealed partial class RequestSender
    {
        private readonly Server _server;

        private bool _closed;

        private readonly ILogger _logger;

        internal RequestSender(Server server, ILogger logger)
        {
            _server = server;
            _logger = logger;
        }

        public void Remove()
        {
            LogRemovingRequestSender();
            _closed = true;
        }

        public async Task<string?>? RequestAsync(string queueName, long timestamp, string type, string data)
        {
            if (_closed)
            {
                throw new InvalidOperationException();
            }

            if (!ValidateQueueName(queueName))
            {
                return null;
            }

            if (!ValidateType(type))
            {
                return null;
            }

            if (!ValidateData(data))
            {
                return null;
            }

            HashSet<RequestHandler> requestHandlers;
            _server._requestHandlersLock.EnterReadLock();
            try
            {
                requestHandlers = _server.GetHandlers(queueName);
            }
            finally
            {
                _server._requestHandlersLock.ExitReadLock();
            }

            var request = new RequestHandler.RequestR(timestamp, type, data);

            foreach (RequestHandler requestHandler in requestHandlers)
            {
                TaskCompletionSource<string?> tcs = requestHandler.Request(request);
                string? response = await tcs.Task.ConfigureAwait(false);

                if (response is not null)
                {
                    return response;
                }
            }

            return null;
        }
    
        [LoggerMessage(Level = LogLevel.Debug, Message = "Removing request sender")]
        private partial void LogRemovingRequestSender();
    }

    private static bool ValidateQueueName(string queueName)
        => !string.IsNullOrWhiteSpace(queueName) && queueName.Length != 0 && !GetValitationRegex1().IsMatch(queueName) && !GetValitationRegex2().IsMatch(queueName);

    private static bool ValidateType(string type)
        => !string.IsNullOrWhiteSpace(type) && type.Length != 0 && !GetValitationRegex1().IsMatch(type) && !GetValitationRegex2().IsMatch(type);

    private static bool ValidateData(string str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] < 32 || str[i] >= 127)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex("[^A-Za-z0-9_\\-]")]
    private static partial Regex GetValitationRegex1();

    [GeneratedRegex("^[^A-Za-z0-9]")]
    private static partial Regex GetValitationRegex2();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding subscriber for queue '{QueueName}'")]
    private partial void LogAddingSubscriber(string QueueName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding publisher")]
    private partial void LogAddingPublisher();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding request handler for queue '{QueueName}'")]
    private partial void LogAddingRequestHandler(string QueueName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding request sender")]
    private partial void LogAddingRequestSender();
}
