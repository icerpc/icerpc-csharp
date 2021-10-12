// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal.Slic;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Internal
{
    /// <summary>A network socket connection based on a <see cref="NetworkSocket"/>.</summary>
    internal sealed class NetworkSocketConnection : INetworkConnection, ISingleStreamConnection
    {
        /// <inheritdoc/>
        public int DatagramMaxReceiveSize => NetworkSocket.DatagramMaxReceiveSize;
        /// <inheritdoc/>
        public bool IsDatagram => NetworkSocket.IsDatagram;
        /// <inheritdoc/>
        public bool IsSecure => NetworkSocket.SslStream != null;
        /// <inheritdoc/>
        public TimeSpan LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        // NetworkSocket is internal to allow the LogNetworkSocketConnection to provide additional information
        // provided by the socket (such as the send or receive buffer sizes).
        internal NetworkSocket NetworkSocket { get; }

        private readonly TimeSpan _defaultIdleTimeout;
        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;
        private SlicConnection? _slicConnection;
        private readonly SlicOptions _slicOptions;

        /// <inheritdoc/>
        public void Close(Exception? exception = null)
        {
            _slicConnection?.Dispose();
            NetworkSocket.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask<(IMultiStreamConnection, NetworkConnectionInformation)> ConnectMultiStreamConnectionAsync(
            CancellationToken cancel)
        {
            (ISingleStreamConnection singleStreamConnection, NetworkConnectionInformation information) =
                await ConnectSingleStreamConnectionAsync(cancel).ConfigureAwait(false);

            if (singleStreamConnection.IsDatagram)
            {
                throw new NotSupportedException("multi-stream connection is not supported with datagram connections");
            }

            // Multi-stream support for a network socket connection is provided by Slic.
            _slicConnection ??= await NetworkConnection.CreateSlicConnectionAsync(
                singleStreamConnection,
                _isServer,
                _defaultIdleTimeout,
                _slicOptions,
                cancel).ConfigureAwait(false);

            // Slic negotiates the idle timeout with the peer.
            return (_slicConnection, information with { IdleTimeout = _slicConnection.IdleTimeout });
        }

        /// <inheritdoc/>
        public async ValueTask<(ISingleStreamConnection, NetworkConnectionInformation)> ConnectSingleStreamConnectionAsync(
            CancellationToken cancel)
        {
            Endpoint endpoint = await NetworkSocket.ConnectAsync(_endpoint, cancel).ConfigureAwait(false);
            X509Certificate? remoteCertificate = NetworkSocket.SslStream?.RemoteCertificate;
            if (_isServer)
            {
                // For a server connection, _endpoint is the local endpoint and the endpoint returned by
                // ConnectAsync is the remote endpoint.
                return (
                    this,
                    new NetworkConnectionInformation(_endpoint, endpoint, _defaultIdleTimeout, remoteCertificate));
            }
            else
            {
                // For a client connection, _endpoint is the remote endpoint and the endpoint returned by
                // ConnectAsync is the local endpoint.
                return (
                    this,
                    new NetworkConnectionInformation(endpoint, _endpoint, _defaultIdleTimeout, remoteCertificate));
            }
        }

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !_isServer &&
            EndpointComparer.ParameterLess.Equals(_endpoint, remoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        async ValueTask<int> ISingleStreamConnection.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await NetworkSocket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            return received;
        }

        /// <inheritdoc/>
        async ValueTask ISingleStreamConnection.WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            await NetworkSocket.SendAsync(buffers, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
        }

        /// <inheritdoc/>
        public override string? ToString() => NetworkSocket.ToString();

        internal NetworkSocketConnection(
            NetworkSocket socket,
            Endpoint endpoint,
            bool isServer,
            TimeSpan defaultIdleTimeout,
            SlicOptions slicOptions)
        {
            NetworkSocket = socket;

            _endpoint = endpoint;
            _isServer = isServer;
            _defaultIdleTimeout = defaultIdleTimeout;
            _slicOptions = slicOptions;
        }
    }
}
