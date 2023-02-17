// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

/// <summary>The Quic multiplexed connection implements an <see cref="IMultiplexedConnection" />.</summary>
internal abstract class QuicMultiplexedConnection : IMultiplexedConnection
{
    private protected QuicConnection? _connection;
    private readonly int _minSegmentSize;
    private readonly MemoryPool<byte> _pool;

    private protected QuicMultiplexedConnection(MultiplexedConnectionOptions options)
    {
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
            throw exception.ToIceRpcException();
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
            throw exception.ToIceRpcException();
        }
    }

    public async ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException();
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
            throw exception.ToIceRpcException();
        }

        return new QuicMultiplexedStream(
            stream,
            isRemote: false,
            _pool,
            _minSegmentSize);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
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
            throw exception.ToIceRpcException();
        }

        return new TransportConnectionInformation(
            localNetworkAddress: _connection.LocalEndPoint,
            remoteNetworkAddress: _connection.RemoteEndPoint,
            _connection.RemoteCertificate);
    }

    internal QuicMultiplexedClientConnection(
        MultiplexedConnectionOptions options,
        QuicClientConnectionOptions quicOptions)
        : base(options) => _quicClientConnectionOptions = quicOptions;
}

internal class QuicMultiplexedServerConnection : QuicMultiplexedConnection
{
    public override Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new TransportConnectionInformation(
            localNetworkAddress: _connection!.LocalEndPoint,
            remoteNetworkAddress: _connection.RemoteEndPoint,
            _connection.RemoteCertificate));

    internal QuicMultiplexedServerConnection(
        QuicConnection connection,
        MultiplexedConnectionOptions options)
        : base(options) => _connection = connection;
}
