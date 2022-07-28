// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexServerTransportDecorator : IDuplexServerTransport
{
    public string Name => _decoratee.Name;

    private const string Kind = "Duplex";
    private readonly IDuplexServerTransport _decoratee;
    private readonly ILogger _logger;

    public IDuplexListener Listen(DuplexListenerOptions options)
    {
        try
        {
            IDuplexListener listener = _decoratee.Listen(options);
            _logger.LogServerTransportListen(Kind, listener.Endpoint);
            return new LogDuplexListenerDecorator(listener, _logger);
        }
        catch (Exception exception)
        {
            _logger.LogServerTransportListenException(exception, Kind, options.Endpoint);
            throw;
        }
    }

    internal LogDuplexServerTransportDecorator(IDuplexServerTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
