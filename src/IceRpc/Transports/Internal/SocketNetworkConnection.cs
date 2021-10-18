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

        private readonly TimeSpan _idleTimeout;
        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;

        public void Close(Exception? exception = null) => NetworkSocket.Dispose();

        public async Task<(INetworkStream?, IMultiplexedNetworkStreamFactory?, NetworkConnectionInformation)> ConnectAsync(
            CancellationToken cancel)
        {
            Endpoint endpoint = await NetworkSocket.ConnectAsync(_endpoint, cancel).ConfigureAwait(false);
            X509Certificate? remoteCertificate = NetworkSocket.SslStream?.RemoteCertificate;

            // For a server connection, _endpoint is the local endpoint and the endpoint returned by
            // ConnectAsync is the remote endpoint. For a client connection it's the contrary.
            return (this, null, new NetworkConnectionInformation(
                    _isServer ? _endpoint : endpoint,
                    _isServer ? endpoint : _endpoint,
                    _idleTimeout,
                     remoteCertificate
                ));
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
            TimeSpan idleTimeout)
        {
            NetworkSocket = socket;

            _endpoint = endpoint;
            _isServer = isServer;
            _idleTimeout = idleTimeout;
        }
    }
}
