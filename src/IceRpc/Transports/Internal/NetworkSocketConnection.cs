// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Internal
{
    /// <summary>A network socket connection based on a <see cref="NetworkSocket"/>.</summary>
    internal sealed class NetworkSocketConnection : INetworkConnection, INetworkStream
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

        /// <inheritdoc/>
        public void Close(Exception? exception = null) => NetworkSocket.Dispose();

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        public async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            Endpoint endpoint = await NetworkSocket.ConnectAsync(_endpoint, cancel).ConfigureAwait(false);
            X509Certificate? remoteCertificate = NetworkSocket.SslStream?.RemoteCertificate;

            // For a server connection, _endpoint is the local endpoint and the endpoint returned by
            // ConnectAsync is the remote endpoint, it's the contrary for a client connection.
            return new NetworkConnectionInformation(
                _isServer ? _endpoint : endpoint,
                _isServer ? endpoint : _endpoint,
                _defaultIdleTimeout,
                remoteCertificate);
        }

        /// <inheritdoc/>
        public Task<IMultiplexedNetworkStreamFactory> GetMultiplexedNetworkStreamFactoryAsync(
            CancellationToken cancel) =>
            Task.FromException<IMultiplexedNetworkStreamFactory>(new NotSupportedException());

        /// <inheritdoc/>
        public INetworkStream GetNetworkStream() => this;

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !_isServer &&
            EndpointComparer.ParameterLess.Equals(_endpoint, remoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        async ValueTask<int> INetworkStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await NetworkSocket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            return received;
        }

        /// <inheritdoc/>
        async ValueTask INetworkStream.WriteAsync(
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
            TimeSpan defaultIdleTimeout)
        {
            NetworkSocket = socket;

            _endpoint = endpoint;
            _isServer = isServer;
            _defaultIdleTimeout = defaultIdleTimeout;
        }
    }
}
