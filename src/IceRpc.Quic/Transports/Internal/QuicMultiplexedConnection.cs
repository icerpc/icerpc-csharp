// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

/// <summary>The Quic multiplexed connection implements an <see cref="IMultiplexedConnection" />.</summary>
internal abstract class QuicMultiplexedConnection : IMultiplexedConnection
{
    public ServerAddress ServerAddress { get; }

    private protected QuicConnection? _connection;
    private readonly int _minSegmentSize;
    private readonly MemoryPool<byte> _pool;

    private protected QuicMultiplexedConnection(ServerAddress serverAddress, MultiplexedConnectionOptions options)
    {
        ServerAddress = serverAddress;
        _minSegmentSize = options.MinSegmentSize;
        _pool = options.Pool;
    }

    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("The Quic connection is not connected.");
        }

        try
        {
            QuicStream stream = await _connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
            return new QuicMultiplexedStream(stream, isRemote: true, _pool, _minSegmentSize);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException("The accept operation failed.");
        }
    }

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        try
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync((long)closeError, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException("The close operation failed.");
        }
    }

    public async ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("The Quic connection is not connected.");
        }

        QuicStream stream;
        try
        {
            stream = await _connection.OpenOutboundStreamAsync(
                bidirectional ? QuicStreamType.Bidirectional : QuicStreamType.Unidirectional,
                cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException("The stream creation failed.");
        }

        return new QuicMultiplexedStream(
            stream,
            isRemote: false,
            _pool,
            _minSegmentSize);
    }

    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? default;
}

internal class QuicMultiplexedClientConnection : QuicMultiplexedConnection
{
    private readonly QuicClientConnectionOptions _quicClientConnectionOptions;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        // Establish the Quic connection.
        try
        {
            _connection = await QuicConnection.ConnectAsync(
                _quicClientConnectionOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException("The connect operation failed.");
        }

        return new TransportConnectionInformation(
            localNetworkAddress: _connection.LocalEndPoint,
            remoteNetworkAddress: _connection.RemoteEndPoint,
            _connection.RemoteCertificate);
    }

    internal QuicMultiplexedClientConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        QuicClientConnectionOptions quicOptions)
        : base(serverAddress, options) => _quicClientConnectionOptions = quicOptions;
}

internal class QuicMultiplexedServerConnection : QuicMultiplexedConnection
{
    public override Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new TransportConnectionInformation(
            localNetworkAddress: _connection!.LocalEndPoint,
            remoteNetworkAddress: _connection.RemoteEndPoint,
            _connection.RemoteCertificate));

    internal QuicMultiplexedServerConnection(
        ServerAddress serverAddress,
        QuicConnection connection,
        MultiplexedConnectionOptions options)
        : base(serverAddress, options) => _connection = connection;
}
