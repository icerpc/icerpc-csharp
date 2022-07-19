// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogMultiplexedListenerDecorator : IMultiplexedListener
{
    private readonly IMultiplexedListener _decoratee;
    private readonly ILogger _logger;

    Endpoint IMultiplexedListener.Endpoint => _decoratee.Endpoint;

    async Task<IMultiplexedConnection> IMultiplexedListener.AcceptAsync()
    {
        try
        {
            IMultiplexedConnection connection = await _decoratee.AcceptAsync().ConfigureAwait(false);
            return new LogMultiplexedConnectionDecorator(connection, _decoratee.Endpoint, isServer: true, _logger);
        }
        catch (ObjectDisposedException)
        {
            // We assume the decoratee is shut down which should not result in an error message.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogListenerAcceptFailed(_decoratee.Endpoint, ex);
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            _decoratee.Dispose();
        }
        finally
        {
            _logger.LogListenerDispose(_decoratee.Endpoint);
        }
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogMultiplexedListenerDecorator(IMultiplexedListener decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
        _logger.LogListenerCreated(_decoratee.Endpoint);
    }
}
