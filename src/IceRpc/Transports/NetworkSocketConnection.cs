// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>Base class for multi-stream connection implementations for <see cref="NetworkSocket"/>.</summary>
    public abstract class NetworkSocketConnection : MultiStreamConnection
    {
        /// <summary>Creates a network socket connection from a network socket.</summary>
        /// <param name="networkSocket">The network socket.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the endpoint
        /// the server is listening on.</param>
        /// <param name="options">The connection options.</param>
        /// <returns>A new network socket connection.</returns>
        public static NetworkSocketConnection FromNetworkSocket(
            NetworkSocket networkSocket,
            Endpoint endpoint,
            ConnectionOptions options) =>
            endpoint.Protocol == Protocol.Ice1 ?
                new Ice1Connection(networkSocket, endpoint, options) :
                new SlicConnection(networkSocket, endpoint, options);

        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation => NetworkSocket.ConnectionInformation;

        /// <summary>The underlying network socket.</summary>
        public NetworkSocket NetworkSocket { get; private set; }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} ({NetworkSocket})";

        /// <inheritdoc/>
        public override async ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Endpoint? remoteEndpoint = await NetworkSocket.AcceptAsync(
                LocalEndpoint!,
                authenticationOptions,
                cancel).ConfigureAwait(false);

            if (remoteEndpoint != null)
            {
                RemoteEndpoint = remoteEndpoint;
            }
        }

        /// <inheritdoc/>
        public override async ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) =>
            LocalEndpoint = await NetworkSocket.ConnectAsync(
                RemoteEndpoint!,
                authenticationOptions,
                cancel).ConfigureAwait(false);

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

        /// <summary>Constructs a connection.</summary>
        /// <param name="networkSocket">The network socket. It can be a client socket or server socket, and the
        /// resulting connection will be likewise a client or server connection.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the endpoint
        /// the server is listening on.</param>
        /// <param name="options">The connection options.</param>
        protected NetworkSocketConnection(
            NetworkSocket networkSocket,
            Endpoint endpoint,
            ConnectionOptions options)
            : base(endpoint, options, networkSocket.Logger) => NetworkSocket = networkSocket;
    }
}
