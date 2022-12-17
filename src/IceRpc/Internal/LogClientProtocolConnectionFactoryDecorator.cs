// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>Provides a log decorator for client protocol connection factory.</summary>
internal class LogClientProtocolConnectionFactoryDecorator : IClientProtocolConnectionFactory
{
    private readonly IClientProtocolConnectionFactory _decoratee;
    private readonly ILogger _logger;

    public IProtocolConnection CreateConnection(ServerAddress serverAddress)
    {
        IProtocolConnection connection = _decoratee.CreateConnection(serverAddress);
        return new LogProtocolConnectionDecorator(connection, remoteNetworkAddress: null, _logger);
    }

    internal LogClientProtocolConnectionFactoryDecorator(
        IClientProtocolConnectionFactory decoratee,
        ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
