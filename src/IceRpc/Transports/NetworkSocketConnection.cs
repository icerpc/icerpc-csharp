// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A network socket connection represents a network connection based on a <see
    /// cref="_socket"/>.</summary>
    public sealed class NetworkSocketConnection : INetworkConnection
    {
        /// <inheritdoc/>
        public bool IsSecure => _socket.SslStream != null;

        /// <summary>The underlying network socket.</summary>
        public INetworkSocket NetworkSocket => _socket;

        /// <summary><c>true</c> for server connections; otherwise, <c>false</c>. A server connection is created
        /// by a server-side listener while a client connection is created from the endpoint by the client-side.
        /// </summary>
        public bool IsServer { get; }

        /// <summary>The local endpoint. The endpoint may not be available until the connection is connected.
        /// </summary>
        public Endpoint? LocalEndpoint { get; private set; }

        /// <summary>The remote endpoint. This endpoint may not be available until the connection is accepted.
        /// </summary>
        public Endpoint? RemoteEndpoint { get; private set; }

        private readonly NetworkSocket _socket;
        private readonly INetworkSocket _decoratedSocket;

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(CancellationToken cancel)
        {
            if (!IsServer)
            {
                LocalEndpoint = await _decoratedSocket.ConnectAsync(RemoteEndpoint!, cancel).ConfigureAwait(false);
            }
            else if (!_decoratedSocket.IsDatagram)
            {
                RemoteEndpoint = await _decoratedSocket.ConnectAsync(LocalEndpoint!, cancel).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _socket.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !IsServer &&
            EndpointComparer.ParameterLess.Equals(remoteEndpoint, RemoteEndpoint) &&
            _decoratedSocket.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        public override string? ToString() => _decoratedSocket.ToString();

        /// <summary>Constructs a connection.</summary>
        /// <param name="networkSocket">The network socket. It can be a client socket or server socket, and
        /// the resulting connection will be likewise a client or server network connection.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the
        /// endpoint the server is listening on.</param>
        /// <param name="isServer">The connection is a server connection.</param>
        /// <param name="logger">The logger.</param>
        public NetworkSocketConnection(NetworkSocket networkSocket, Endpoint endpoint, bool isServer, ILogger logger)
        {
            IsServer = isServer;
            LocalEndpoint = IsServer ? endpoint : null;
            RemoteEndpoint = IsServer ? null : endpoint;

            _socket = networkSocket;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _decoratedSocket = new LogNetworkSocketDecorator(networkSocket, logger);
            }
            else
            {
                _decoratedSocket = networkSocket;
            }
        }
    }
}
