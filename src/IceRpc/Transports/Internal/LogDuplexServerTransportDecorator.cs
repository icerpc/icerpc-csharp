// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexServerTransportDecorator : IDuplexServerTransport
{
    public string Name => _decoratee.Name;

    private const string Kind = "Duplex";
    private readonly IDuplexServerTransport _decoratee;
    private readonly ILogger _logger;

    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        try
        {
            IListener<IDuplexConnection> listener = _decoratee.Listen(serverAddress, options, serverAuthenticationOptions);
            _logger.LogServerTransportListen(Kind, listener.ServerAddress);
            return new LogListenerDecorator<IDuplexConnection>(listener, Kind, _logger);
        }
        catch (Exception exception)
        {
            _logger.LogServerTransportListenException(exception, Kind, serverAddress);
            throw;
        }
    }

    internal LogDuplexServerTransportDecorator(IDuplexServerTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
