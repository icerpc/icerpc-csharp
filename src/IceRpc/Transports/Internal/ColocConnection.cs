// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal
{
    /// <summary>The network connection class the colocated transport.</summary>
    internal class ColocConnection : INetworkConnection, ISingleStreamConnection
    {
        public int DatagramMaxReceiveSize => throw new InvalidOperationException();

        public bool IsDatagram => false;

        public bool IsSecure => true;

        public bool IsServer { get; }

        public Endpoint? LocalEndpoint { get; }

        public Endpoint? RemoteEndpoint { get; }

        private readonly ILogger _logger;
        private readonly MultiStreamOptions _options;
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private ReadOnlyMemory<byte> _receivedBuffer;
        private SlicConnection? _slicConnection;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        public ValueTask ConnectAsync(CancellationToken cancel) => default;

        public void Dispose() => _slicConnection?.Dispose();

        public IMultiStreamConnection GetMultiStreamConnection()
        {
            _slicConnection = new SlicConnection(
                this,
                IsServer,
                _logger,
                new SlicOptions()
                {
                    BidirectionalStreamMaxCount = _options.BidirectionalStreamMaxCount,
                    UnidirectionalStreamMaxCount = _options.UnidirectionalStreamMaxCount
                });
            return _slicConnection;
        }

        public ISingleStreamConnection GetSingleStreamConnection() => this;

        public bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }
            return !IsServer;
        }

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_receivedBuffer.Length == 0)
            {
                _receivedBuffer = await _reader.ReadAsync(cancel).ConfigureAwait(false);
            }

            if (_receivedBuffer.Length > buffer.Length)
            {
                _receivedBuffer[0..buffer.Length].CopyTo(buffer);
                _receivedBuffer = _receivedBuffer[buffer.Length..];
                return buffer.Length;
            }
            else
            {
                _receivedBuffer.CopyTo(buffer[0.._receivedBuffer.Length]);
                _receivedBuffer = ReadOnlyMemory<byte>.Empty;
                return _receivedBuffer.Length;
            }
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            _writer.WriteAsync(buffer, cancel);

        public ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            SendAsync(buffers.ToArray(), cancel);

        internal ColocConnection(
            Endpoint endpoint,
            bool isServer,
            MultiStreamOptions options,
            ChannelWriter<ReadOnlyMemory<byte>> writer,
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ILogger logger)
        {
            IsServer = isServer;
            LocalEndpoint = endpoint;
            RemoteEndpoint = endpoint;
            _options = options;
            _reader = reader;
            _writer = writer;
            _logger = logger;
        }
    }
}
