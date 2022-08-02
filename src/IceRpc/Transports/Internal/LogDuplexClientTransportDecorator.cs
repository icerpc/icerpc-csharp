// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexClientTransportDecorator : IDuplexClientTransport
{
    private readonly IDuplexClientTransport _decoratee;
    private readonly ILogger _logger;

    public string Name => _decoratee.Name;

    public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

    // This decorator does not log anything, it only provides a decorated duplex connection.
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions) =>
        new LogDuplexConnectionDecorator(
            _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions),
            _logger);

    internal LogDuplexClientTransportDecorator(IDuplexClientTransport decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
