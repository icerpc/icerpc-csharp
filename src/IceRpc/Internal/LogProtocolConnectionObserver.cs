// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

/// <summary>The log implementation of <see cref="IProtocolConnectionObserver"/>.</summary>
internal sealed class LogProtocolConnectionObserver : IProtocolConnectionObserver
{
    private readonly ILogger _logger;
    private TransportConnectionInformation _connectionInformation;

    public void Connected(ServerAddress serverAddress, TransportConnectionInformation connectionInformation)
    {
        _connectionInformation = connectionInformation;
        _logger.LogConnectionConnect(
            serverAddress,
            connectionInformation.LocalNetworkAddress,
            connectionInformation.RemoteNetworkAddress);
    }

    public void ConnectException(Exception exception, ServerAddress serverAddress) =>
        _logger.LogConnectionConnectException(exception, serverAddress);

    public void Disposed(ServerAddress serverAddress) =>
        _logger.LogConnectionDispose(
            serverAddress,
            _connectionInformation.LocalNetworkAddress,
            _connectionInformation.RemoteNetworkAddress);

    public void ShutDown(string message, ServerAddress serverAddress) =>
        _logger.LogConnectionShutdown(
            message,
            serverAddress,
            _connectionInformation.LocalNetworkAddress,
            _connectionInformation.RemoteNetworkAddress);

    internal LogProtocolConnectionObserver(ILogger logger) => _logger = logger;
}
