// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexServerTransportDecorator : IDuplexServerTransport
{
    private readonly IDuplexServerTransport _decoratee;
    private readonly ILogger _logger;

    public string Name => _decoratee.Name;

    public IDuplexListener Listen(DuplexListenerOptions options)
    {
        try
        {
            IDuplexListener listener = _decoratee.Listen(options);
            _logger.LogServerTransportListen(listener.Endpoint);
            return new LogDuplexListenerDecorator(listener, _logger);
        }
        catch (Exception exception)
        {
            _logger.LogServerTransportListenException(exception, options.Endpoint);
            throw;
        }
    }

    internal LogDuplexServerTransportDecorator(IDuplexServerTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
