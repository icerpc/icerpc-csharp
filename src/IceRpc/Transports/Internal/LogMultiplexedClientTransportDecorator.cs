// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

internal sealed class LogMultiplexedClientTransportDecorator : IMultiplexedClientTransport
{
    private readonly IMultiplexedClientTransport _decoratee;
    private readonly ILogger _logger;

    public string Name => _decoratee.Name;

    public bool CheckParams(Endpoint endpoint) => _decoratee.CheckParams(endpoint);

    // This decorator does not log anything, it only provides a decorated multiplex connection.
    public IMultiplexedConnection CreateConnection(
        Endpoint endpoint,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions) => new LogMultiplexedConnectionDecorator(
            _decoratee.CreateConnection(endpoint, options, clientAuthenticationOptions),
            _logger);

    internal LogMultiplexedClientTransportDecorator(IMultiplexedClientTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
