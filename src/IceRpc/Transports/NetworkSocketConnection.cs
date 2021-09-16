// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A network socket connection based on a <see cref="NetworkSocket"/>.</summary>
    public sealed class NetworkSocketConnection : INetworkConnection, ISingleStreamConnection
    {
        /// <summary>When this connection is a datagram connection, the maximum size of a received datagram.
        /// </summary>
        public int DatagramMaxReceiveSize => NetworkSocket.DatagramMaxReceiveSize;

        /// <summary><c>true</c> for a datagram network connection; <c>false</c> otherwise.</summary>
        public bool IsDatagram => NetworkSocket.IsDatagram;

        /// <inheritdoc/>
        public bool IsSecure => NetworkSocket.SslStream != null;

        /// <inheritdoc/>
        public bool IsServer { get; }

        /// <inheritdoc/>
        public Endpoint? LocalEndpoint { get; private set; }

        internal NetworkSocket NetworkSocket { get; }

        /// <inheritdoc/>
        public Endpoint? RemoteEndpoint { get; private set; }

        private readonly ILogger _logger;
        private Internal.SlicConnection? _slicConnection;
        private readonly SlicOptions _slicOptions;

        /// <summary>Creates a new network socket connection based on <see cref="NetworkSocket"/></summary>
        /// <param name="socket">The network socket. It can be a client socket or server socket, and
        /// the resulting connection will be likewise a client or server network connection.</param>
        /// <param name="endpoint">For a client connection, the remote endpoint; for a server connection, the
        /// endpoint the server is listening on.</param>
        /// <param name="isServer">The connection is a server connection.</param>
        /// <param name="slicOptions">The Slic options.</param>
        /// <param name="logger">The logger.</param>
        public NetworkSocketConnection(
            NetworkSocket socket,
            Endpoint endpoint,
            bool isServer,
            SlicOptions slicOptions,
            ILogger logger)
        {
            IsServer = isServer;
            LocalEndpoint = IsServer ? endpoint : null;
            RemoteEndpoint = IsServer ? null : endpoint;
            _logger = logger;
            _slicOptions = slicOptions;

            NetworkSocket = socket;
        }

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(CancellationToken cancel)
        {
            if (!IsServer)
            {
                LocalEndpoint = await NetworkSocket.ConnectAsync(RemoteEndpoint!, cancel).ConfigureAwait(false);
            }
            else if (!NetworkSocket.IsDatagram)
            {
                RemoteEndpoint = await NetworkSocket.ConnectAsync(LocalEndpoint!, cancel).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _slicConnection?.Dispose();
            NetworkSocket.Dispose();
        }

        /// <inheritdoc/>
        public ISingleStreamConnection GetSingleStreamConnection() => this;

        /// <inheritdoc/>
        public IMultiStreamConnection GetMultiStreamConnection()
        {
            _slicConnection = new Internal.SlicConnection(this, IsServer, _logger, _slicOptions);
            return _slicConnection;
        }

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !IsServer &&
            EndpointComparer.ParameterLess.Equals(remoteEndpoint, RemoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        ValueTask<int> ISingleStreamConnection.ReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            NetworkSocket.ReceiveAsync(buffer, cancel);

        /// <inheritdoc/>
        ValueTask ISingleStreamConnection.SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            NetworkSocket.SendAsync(buffer, cancel);

        /// <inheritdoc/>
        ValueTask ISingleStreamConnection.SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel) =>
            NetworkSocket.SendAsync(buffers, cancel);

        /// <inheritdoc/>
        public override string? ToString() => NetworkSocket.ToString();
    }
}
