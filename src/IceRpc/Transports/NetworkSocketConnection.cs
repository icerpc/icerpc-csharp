// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>A transport connection based on a <see cref="NetworkSocket"/>.</summary>
    public sealed class NetworkSocketConnection : ITransportConnection
    {
        /// <inheritdoc/>
        public bool IsDatagram => NetworkSocket.IsDatagram;

        /// <inheritdoc/>
        public bool IsSecure => NetworkSocket.SslStream != null;

        /// <summary>The underlying network socket.</summary>
        public NetworkSocket NetworkSocket { get; private set; }

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

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(CancellationToken cancel)
        {
            if (IsServer)
            {
                RemoteEndpoint = await NetworkSocket.ConnectAsync(LocalEndpoint!, cancel).ConfigureAwait(false);
            }
            else
            {
                LocalEndpoint = await NetworkSocket.ConnectAsync(RemoteEndpoint!, cancel).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            NetworkSocket.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !IsServer &&
            EndpointComparer.ParameterLess.Equals(remoteEndpoint, RemoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        /// <summary>Constructs a connection.</summary>
        /// <param name="networkSocket">The network socket. It can be a client socket or server socket, and the
        /// resulting connection will be likewise a client or server connection.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the endpoint
        /// the server is listening on.</param>
        /// <param name="isServer">The connection is a server connection.</param>
        public NetworkSocketConnection(NetworkSocket networkSocket, Endpoint endpoint, bool isServer)
        {
            IsServer = isServer;
            LocalEndpoint = IsServer ? endpoint : null;
            RemoteEndpoint = IsServer ? null : endpoint;
            NetworkSocket = networkSocket;
        }
    }
}
