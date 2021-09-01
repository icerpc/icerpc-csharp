// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Base class for multi-stream connection implementations that use <see cref="NetworkSocket"/>.</summary>
    public abstract class NetworkSocketConnection : MultiStreamConnection
    {
        /// <inheritdoc/>
        public override bool IsDatagram => NetworkSocket.IsDatagram;

        /// <inheritdoc/>
        public override bool IsSecure => NetworkSocket.SslStream != null;

        /// <summary>Creates a network socket connection from a network socket.</summary>
        /// <param name="networkSocket">The network socket.</param>
        /// <param name="isServer"><c>true</c> if the connection is a server connection, <c>false</c> otherwise.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the endpoint
        /// the server is listening on.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>A new network socket connection.</returns>
        public static NetworkSocketConnection FromNetworkSocket(
            NetworkSocket networkSocket,
            Endpoint endpoint,
            bool isServer,
            MultiStreamOptions options) =>
            endpoint.Protocol == Protocol.Ice1 ?
                new Ice1Connection(networkSocket, endpoint, isServer, options) :
                new SlicConnection(networkSocket, endpoint, isServer, options);

        /// <summary>The underlying network socket.</summary>
        public NetworkSocket NetworkSocket { get; private set; }

        /// <inheritdoc/>
        public override async ValueTask ConnectAsync(CancellationToken cancel)
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
        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !IsServer &&
            EndpointComparer.ParameterLess.Equals(remoteEndpoint, RemoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        /// <summary>Constructs a connection.</summary>
        /// <param name="networkSocket">The network socket. It can be a client socket or server socket, and the
        /// resulting connection will be likewise a client or server connection.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the endpoint
        /// the server is listening on.</param>
        /// <param name="isServer">The connection is a server connection.</param>
        protected NetworkSocketConnection(
            NetworkSocket networkSocket,
            Endpoint endpoint,
            bool isServer)
            : base(endpoint, isServer, networkSocket.Logger) => NetworkSocket = networkSocket;

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // First dispose of the underlying connection otherwise base.Dispose() which releases the stream can trigger
            // additional data to be sent of the stream release sends data (which is the case for SlicStream).
            if (disposing)
            {
                NetworkSocket.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
