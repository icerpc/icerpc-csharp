// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Net.Quic;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Quic.Internal;

/// <summary>The Quic multiplexed connection implements an <see cref="IMultiplexedConnection" />.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
internal abstract class QuicMultiplexedConnection : IMultiplexedConnection
{
    private protected QuicConnection? _connection;
    private volatile bool _isClosed;
    private volatile bool _isDisposed;
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
            return new QuicMultiplexedStream(stream, isRemote: true, _pool, _minSegmentSize, ThrowIfClosedOrDisposed);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
    }

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        try
        {
            _isClosed = true;
            if (_connection is not null)
            {
                await _connection.CloseAsync((long)closeError, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
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
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (ObjectDisposedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        return new QuicMultiplexedStream(
            stream,
            isRemote: false,
            _pool,
            _minSegmentSize,
            ThrowIfClosedOrDisposed);
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Workaround for https://github.com/dotnet/runtime/issues/78641.
            }
        }
    }

    private void ThrowIfClosedOrDisposed()
    {
        if (_isClosed)
        {
            throw new IceRpcException(IceRpcError.ConnectionAborted, "The connection was closed.");
        }
        else if (_isDisposed)
        {
            throw new IceRpcException(IceRpcError.ConnectionAborted, "The connection was disposed.");
        }
    }
}

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
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
        catch (SocketException exception)
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

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
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
