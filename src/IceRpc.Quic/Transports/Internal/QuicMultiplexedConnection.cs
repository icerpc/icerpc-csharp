// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Net.Quic;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal;

/// <summary>The Quic multiplexed connection implements an <see cref="IMultiplexedConnection" />.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
            throw new InvalidOperationException("the Quic connection is not connected");
        }

        try
        {
            QuicStream stream = await _connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
            return new QuicMultiplexedStream(stream, isRemote: true, _pool, _minSegmentSize);
        }
        catch (QuicException exception)
        {
            throw exception.ToTransportException();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TransportException(TransportErrorCode.Unspecified, exception);
        }
    }

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    public async Task CloseAsync(ulong applicationErrorCode, CancellationToken cancellationToken)
    {
        try
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync((long)applicationErrorCode, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (QuicException exception)
        {
            throw exception.ToTransportException();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TransportException(TransportErrorCode.Unspecified, exception);
        }
    }

    public async ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("the Quic connection is not connected");
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
            throw exception.ToTransportException();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TransportException(TransportErrorCode.Unspecified, exception);
        }

        return new QuicMultiplexedStream(
            stream,
            isRemote: false,
            _pool,
            _minSegmentSize);
    }

    public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? default;
}

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
            throw exception.ToTransportException();
        }
        catch (AuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TransportException(TransportErrorCode.Unspecified, exception);
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

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
