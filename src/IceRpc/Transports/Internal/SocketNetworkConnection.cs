// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Internal
{
    /// <summary>A network socket connection based on a <see cref="NetworkSocket"/>.</summary>
    internal sealed class SocketNetworkConnection : INetworkConnection, INetworkStream
    {
        public int DatagramMaxReceiveSize => NetworkSocket.DatagramMaxReceiveSize;
        public bool IsDatagram => NetworkSocket.IsDatagram;
        public bool IsSecure => NetworkSocket.SslStream != null;
        public TimeSpan LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        // NetworkSocket is internal to allow the LogNetworkSocketConnection to provide additional information
        // provided by the socket (such as the send or receive buffer sizes).
        internal NetworkSocket NetworkSocket { get; }

        private readonly TimeSpan _defaultIdleTimeout;
        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;
        private SlicMultiplexedNetworkStreamFactory? _slicConnection;
        private readonly SlicOptions _slicOptions;

        public void Close(Exception? exception = null)
        {
            _slicConnection?.Dispose();
            NetworkSocket.Dispose();
        }

        public async Task<(INetworkStream?, IMultiplexedNetworkStreamFactory?, NetworkConnectionInformation)> ConnectAsync(
            bool multiplexed,
            CancellationToken cancel)
        {
            Endpoint endpoint = await NetworkSocket.ConnectAsync(_endpoint, cancel).ConfigureAwait(false);
            X509Certificate? remoteCertificate = NetworkSocket.SslStream?.RemoteCertificate;

            // For a server connection, _endpoint is the local endpoint and the endpoint returned by
            // ConnectAsync is the remote endpoint. For a client connection it's the contrary.
            var information = new NetworkConnectionInformation(
                    _isServer ? _endpoint : endpoint,
                    _isServer ? endpoint : _endpoint,
                    _defaultIdleTimeout,
                     remoteCertificate
                );

            if (multiplexed)
            {
                _slicConnection ??= await NetworkConnection.CreateSlicConnectionAsync(
                    this,
                    _isServer,
                    _defaultIdleTimeout,
                    _slicOptions,
                    cancel).ConfigureAwait(false);
                return (null, _slicConnection, information with { IdleTimeout = _slicConnection.IdleTimeout });
            }
            else
            {
                return (this, null, information);
            }
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !_isServer &&
            EndpointComparer.ParameterLess.Equals(_endpoint, remoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        async ValueTask<int> INetworkStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await NetworkSocket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            return received;
        }

        async ValueTask INetworkStream.WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            await NetworkSocket.SendAsync(buffers, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
        }

        public override string? ToString() => NetworkSocket.ToString();

        internal SocketNetworkConnection(
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
