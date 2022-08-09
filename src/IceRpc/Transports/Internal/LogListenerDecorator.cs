// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogListenerDecorator<T> : IListener<T>
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private readonly IListener<T> _decoratee;
    private readonly ILogger _logger;

    private readonly string _kind;

    public async Task<T> AcceptAsync()
    {
        T connection;
        try
        {
            connection = await _decoratee.AcceptAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // We assume the decoratee is shut down which should not result in an error message.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogListenerAcceptException(exception, _kind, _decoratee.ServerAddress);
            throw;
        }

        _logger.LogListenerAccept(_kind, _decoratee.ServerAddress);
        return connection;
    }

    public void Dispose()
    {
        _decoratee.Dispose();
        _logger.LogListenerDispose(_kind, _decoratee.ServerAddress);
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogListenerDecorator(IListener<T> decoratee, string kind, ILogger logger)
    {
        _decoratee = decoratee;
        _kind = kind;
        _logger = logger;
    }
}
