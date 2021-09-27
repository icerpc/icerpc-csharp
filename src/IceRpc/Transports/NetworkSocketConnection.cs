// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal.Slic;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A network socket connection based on a <see cref="NetworkSocket"/>.</summary>
    public sealed class NetworkSocketConnection : INetworkConnection, ISingleStreamConnection
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
        public bool IsServer { get; }

        /// <inheritdoc/>
        public TimeSpan LastActivity => _slicConnection?.LastActivity ?? TimeSpan.FromMilliseconds(_lastActivity);

        /// <inheritdoc/>
        public Endpoint? LocalEndpoint { get; private set; }

        /// <inheritdoc/>
        public Endpoint? RemoteEndpoint { get; private set; }

        internal NetworkSocket NetworkSocket { get; }

        private readonly TimeSpan _idleTimeout;
        private long _lastActivity;
        private readonly ILogger _logger;
        private SlicConnection? _slicConnection;
        private readonly SlicOptions _slicOptions;

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
        public ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(CancellationToken cancel) => new(this);

        /// <inheritdoc/>
        public async ValueTask<IMultiStreamConnection> GetMultiStreamConnectionAsync(CancellationToken cancel)
        {
            _slicConnection = await NetworkConnection.CreateSlicConnection(
                this,
                IsServer,
                _idleTimeout,
                _slicOptions,
                _logger,
                cancel).ConfigureAwait(false);
            return _slicConnection;
        }

        /// <inheritdoc/>
        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            !IsServer &&
            EndpointComparer.ParameterLess.Equals(remoteEndpoint, RemoteEndpoint) &&
            NetworkSocket.HasCompatibleParams(remoteEndpoint);

        /// <inheritdoc/>
        async ValueTask<int> ISingleStreamConnection.ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await NetworkSocket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            return received;
        }

        /// <inheritdoc/>
        async ValueTask ISingleStreamConnection.SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            await NetworkSocket.SendAsync(buffer, cancel).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
        }

        /// <inheritdoc/>
        ValueTask ISingleStreamConnection.SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel) =>
            NetworkSocket.SendAsync(buffers, cancel);

        /// <inheritdoc/>
        public override string? ToString() => NetworkSocket.ToString();

        internal NetworkSocketConnection(
            NetworkSocket socket,
            Endpoint endpoint,
            bool isServer,
            TimeSpan idleTimeout,
            SlicOptions slicOptions,
            ILogger logger)
        {
            IsServer = isServer;
            LocalEndpoint = IsServer ? endpoint : null;
            RemoteEndpoint = IsServer ? null : endpoint;
            _idleTimeout = idleTimeout;
            _logger = logger;
            _slicOptions = slicOptions;

            NetworkSocket = socket;
        }
    }
}
