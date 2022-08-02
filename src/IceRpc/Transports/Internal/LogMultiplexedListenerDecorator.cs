// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogMultiplexedListenerDecorator : IMultiplexedListener
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    private const string Kind = "Multiplexed";
    private readonly IMultiplexedListener _decoratee;
    private readonly ILogger _logger;

    public async Task<IMultiplexedConnection> AcceptAsync()
    {
        IMultiplexedConnection connection;
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
            _logger.LogListenerAcceptException(exception, Kind, _decoratee.ServerAddress);
            throw;
        }

        _logger.LogListenerAccept(Kind, _decoratee.ServerAddress);
        return new LogMultiplexedConnectionDecorator(connection, _logger);
    }

    public void Dispose()
    {
        _decoratee.Dispose();
        _logger.LogListenerDispose(Kind, _decoratee.ServerAddress);
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogMultiplexedListenerDecorator(IMultiplexedListener decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
