// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

internal sealed class LogMultiplexedServerTransportDecorator : IMultiplexedServerTransport
{
    public string Name => _decoratee.Name;

    private const string Kind = "Multiplexed";
    private readonly IMultiplexedServerTransport _decoratee;
    private readonly ILogger _logger;

    public IMultiplexedListener Listen(
        Endpoint endpoint,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        try
        {
            IMultiplexedListener listener = _decoratee.Listen(endpoint, options, serverAuthenticationOptions);
            _logger.LogServerTransportListen(Kind, listener.Endpoint);
            return new LogMultiplexedListenerDecorator(listener, _logger);
        }
        catch (Exception exception)
        {
            _logger.LogServerTransportListenException(exception, Kind, endpoint);
            throw;
        }
    }

    internal LogMultiplexedServerTransportDecorator(IMultiplexedServerTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
