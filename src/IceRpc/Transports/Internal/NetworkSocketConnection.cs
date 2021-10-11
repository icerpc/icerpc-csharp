// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal.Slic;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>A network socket connection based on a <see cref="NetworkSocket"/>.</summary>
    internal sealed class NetworkSocketConnection : INetworkConnection, ISingleStreamConnection
    {
        /// <inheritdoc/>
        public int DatagramMaxReceiveSize => NetworkSocket.DatagramMaxReceiveSize;
        /// <inheritdoc/>
        public TimeSpan IdleTimeout => _slicConnection?.IdleTimeout ?? _idleTimeout;
        /// <inheritdoc/>
        public bool IsDatagram => NetworkSocket.IsDatagram;
        /// <inheritdoc/>
        public bool IsSecure => NetworkSocket.SslStream != null;
        /// <inheritdoc/>
        public TimeSpan LastActivity => TimeSpan.FromMilliseconds(_lastActivity);
        /// <inheritdoc/>
        public Endpoint? LocalEndpoint { get; private set; }
        /// <inheritdoc/>
        public Endpoint? RemoteEndpoint { get; private set; }

        internal NetworkSocket NetworkSocket { get; }

        private readonly TimeSpan _idleTimeout;
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
        public async ValueTask<IMultiStreamConnection> ConnectMultiStreamConnectionAsync(CancellationToken cancel)
        {
            // Multi-stream support for a network socket connection is provided by Slic.
            _slicConnection ??= await NetworkConnection.CreateSlicConnectionAsync(
                await ConnectSingleStreamConnectionAsync(cancel).ConfigureAwait(false),
                _isServer,
                _idleTimeout,
                _slicOptions,
                cancel).ConfigureAwait(false);
            return _slicConnection;
        }

        /// <inheritdoc/>
        public async ValueTask<ISingleStreamConnection> ConnectSingleStreamConnectionAsync(CancellationToken cancel)
        {
            if (!_isServer)
            {
                LocalEndpoint = await NetworkSocket.ConnectAsync(RemoteEndpoint!, cancel).ConfigureAwait(false);
            }
            else if (!NetworkSocket.IsDatagram)
            {
                RemoteEndpoint = await NetworkSocket.ConnectAsync(LocalEndpoint!, cancel).ConfigureAwait(false);
            }
            return this;
        }

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !_isServer &&
            EndpointComparer.ParameterLess.Equals(remoteEndpoint, RemoteEndpoint) &&
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
            TimeSpan idleTimeout,
            SlicOptions slicOptions)
        {
            LocalEndpoint = isServer ? endpoint : null;
            RemoteEndpoint = isServer ? null : endpoint;
            NetworkSocket = socket;

            _isServer = isServer;
            _idleTimeout = idleTimeout;
            _slicOptions = slicOptions;
        }
    }
}
