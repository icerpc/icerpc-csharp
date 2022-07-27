// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogMultiplexedClientTransportDecorator : IMultiplexedClientTransport
{
    private readonly IMultiplexedClientTransport _decoratee;
    private readonly ILogger _logger;

    public string Name => _decoratee.Name;

    public bool CheckParams(Endpoint endpoint) => _decoratee.CheckParams(endpoint);

    // This decorators does not log anything, it only provides a decorated duplex connection.
    public IMultiplexedConnection CreateConnection(MultiplexedClientConnectionOptions options) =>
        new LogMultiplexedConnectionDecorator(_decoratee.CreateConnection(options), _logger);

    internal LogMultiplexedClientTransportDecorator(IMultiplexedClientTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
