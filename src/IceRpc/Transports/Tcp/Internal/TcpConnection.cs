// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace IceRpc.Transports.Tcp.Internal;

/// <summary>Implements <see cref="IDuplexConnection" /> for tcp with or without TLS.</summary>
/// <remarks>Unlike Coloc, the Tcp transport is not a "checked" transport, which means it does not need to detect
/// violations of the duplex transport contract or report such violations. It assumes its clients are sufficiently well
/// tested to never violate this contract. As a result, this implementation does not throw
/// <see cref="InvalidOperationException" />.</remarks>
internal abstract class TcpConnection : IDuplexConnection
{
    internal abstract Socket Socket { get; }

    internal abstract SslStream? SslStream { get; }

    private protected volatile bool _isDisposed;

    // The MaxDataSize of the SSL implementation.
    private const int MaxSslDataSize = 16 * 1024;

    private bool _isShutdown;
    private readonly int _maxSslBufferSize;
    private readonly int _minSegmentSize;
    private readonly MemoryPool<byte> _pool;
    private readonly List<ArraySegment<byte>> _segments = new();

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return ConnectAsyncCore(cancellationToken);
    }

    public void Dispose()
    {
        _isDisposed = true;

        if (SslStream is SslStream sslStream)
        {
            sslStream.Dispose();
        }

        // If shutdown was called, we can just dispose the connection to complete the graceful TCP closure. Otherwise,
        // we abort the TCP connection to ensure the connection doesn't end up in the TIME_WAIT state.
        if (_isShutdown)
        {
            Socket.Dispose();
        }
        else
        {
            Socket.Close(0);
        }
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return buffer.Length > 0 ? PerformReadAsync() :
            throw new ArgumentException($"The {nameof(buffer)} cannot be empty.", nameof(buffer));

        async ValueTask<int> PerformReadAsync()
        {
            try
            {
                return SslStream is SslStream sslStream ?
                    await SslStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) :
                    await Socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw exception.ToIceRpcException();
            }
            catch (SocketException exception)
            {
                throw exception.ToIceRpcException();
            }
        }
    }

    public Task ShutdownWriteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            try
            {
                if (SslStream is SslStream sslStream)
                {
                    Task shutdownTask = sslStream.ShutdownAsync();

                    try
                    {
                        await shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        await AbortAndObserveAsync(shutdownTask).ConfigureAwait(false);
                        throw;
                    }
                }

                // Shutdown the socket send side to send a TCP FIN packet. We don't close the read side because we want
                // to be notified when the peer shuts down it's side of the socket (through the ReceiveAsync call).
                Socket.Shutdown(SocketShutdown.Send);

                // If shutdown is successful mark the connection as shutdown to ensure Dispose won't reset the TCP
                // connection.
                _isShutdown = true;
            }
            catch (IOException exception)
            {
                throw exception.ToIceRpcException();
            }
            catch (SocketException exception)
            {
                throw exception.ToIceRpcException();
            }
        }
    }

    public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return buffers.Count > 0 ? PerformWriteAsync() :
            throw new ArgumentException($"The {nameof(buffers)} list cannot be empty.", nameof(buffers));

        async ValueTask PerformWriteAsync()
        {
            try
            {
                if (SslStream is SslStream sslStream)
                {
                    if (buffers.Count == 1)
                    {
                        await sslStream.WriteAsync(buffers[0], cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Coalesce leading small buffers up to _maxSslBufferSize. We don't coalesce trailing buffers
                        // as we assume they are large enough.
                        int index = 0;
                        int writeBufferSize = 0;
                        do
                        {
                            ReadOnlyMemory<byte> buffer = buffers[index];
                            if (writeBufferSize + buffer.Length <= _maxSslBufferSize)
                            {
                                index++;
                                writeBufferSize += buffer.Length;
                            }
                            else
                            {
                                break; // while
                            }
                        }
                        while (index < buffers.Count);

                        if (index == 1)
                        {
                            // There is no point copying only the first buffer into another buffer.
                            index = 0;
                        }
                        else if (writeBufferSize > 0)
                        {
                            Debug.Assert(writeBufferSize <= _minSegmentSize);
                            using IMemoryOwner<byte> writeBufferOwner = _pool.Rent(_minSegmentSize);
                            Memory<byte> writeBuffer = writeBufferOwner.Memory[0..writeBufferSize];
                            int offset = 0;
                            for (int i = 0; i < index; ++i)
                            {
                                ReadOnlyMemory<byte> buffer = buffers[i];
                                buffer.CopyTo(writeBuffer[offset..]);
                                offset += buffer.Length;
                            }

                            // Send the "coalesced" initial buffers
                            await sslStream.WriteAsync(writeBuffer, cancellationToken).ConfigureAwait(false);
                        }

                        // Send the remaining buffers one by one
                        for (int i = index; i < buffers.Count; ++i)
                        {
                            await sslStream.WriteAsync(buffers[i], cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    if (buffers.Count == 1)
                    {
                        _ = await Socket.SendAsync(buffers[0], SocketFlags.None, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        _segments.Clear();
                        foreach (ReadOnlyMemory<byte> memory in buffers)
                        {
                            if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
                            {
                                _segments.Add(segment);
                            }
                            else
                            {
                                throw new ArgumentException(
                                    $"The {nameof(buffers)} must be backed by arrays.",
                                    nameof(buffers));
                            }
                        }

                        Task sendTask = Socket.SendAsync(_segments, SocketFlags.None);

                        try
                        {
                            await sendTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            await AbortAndObserveAsync(sendTask).ConfigureAwait(false);
                            throw;
                        }
                    }
                }
            }
            catch (IOException exception)
            {
                throw exception.ToIceRpcException();
            }
            catch (SocketException exception)
            {
                throw exception.ToIceRpcException();
            }
        }
    }

    private protected TcpConnection(MemoryPool<byte> pool, int minimumSegmentSize)
    {
        _pool = pool;
        _minSegmentSize = minimumSegmentSize;

        // When coalescing leading buffers in WriteAsync, we don't want to copy into a buffer greater than the standard
        // segment size in the memory pool (minimumSegmentSize, by default 4K) or greater than MaxSslDataSize (16K).
        _maxSslBufferSize = Math.Min(minimumSegmentSize, MaxSslDataSize);
    }

    private protected abstract Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken);

    /// <summary>Aborts the connection and then observes the exception of the provided task.</summary>
    private async Task AbortAndObserveAsync(Task task)
    {
        Socket.Close(0);
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // observe exception
        }
    }
}

internal class TcpClientConnection : TcpConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly EndPoint _addr;
    private readonly SslClientAuthenticationOptions? _authenticationOptions;

    private SslStream? _sslStream;

    internal TcpClientConnection(
        ServerAddress serverAddress,
        SslClientAuthenticationOptions? authenticationOptions,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        TcpClientTransportOptions options)
        : base(pool, minimumSegmentSize)
    {
        _addr = IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress) ?
            new IPEndPoint(ipAddress, serverAddress.Port) :
            new DnsEndPoint(serverAddress.Host, serverAddress.Port);

        _authenticationOptions = authenticationOptions;

        // When using IPv6 address family we use the socket constructor without AddressFamily parameter to ensure
        // dual-mode socket are used in platforms that support them.
        Socket = ipAddress?.AddressFamily == AddressFamily.InterNetwork ?
            new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) :
            new Socket(SocketType.Stream, ProtocolType.Tcp);

        try
        {
            if (options.LocalNetworkAddress is IPEndPoint localNetworkAddress)
            {
                Socket.Bind(localNetworkAddress);
            }

            if (options.ReceiveBufferSize is int receiveSize)
            {
                Socket.ReceiveBufferSize = receiveSize;
            }
            if (options.SendBufferSize is int sendSize)
            {
                Socket.SendBufferSize = sendSize;
            }

            Socket.NoDelay = true;
        }
        catch (SocketException exception)
        {
            Socket.Dispose();
            throw exception.ToIceRpcException();
        }
        catch
        {
            Socket.Dispose();
            throw;
        }
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        try
        {
            Debug.Assert(Socket is not null);

            // Connect to the peer.
            await Socket.ConnectAsync(_addr, cancellationToken).ConfigureAwait(false);

            // Workaround: a canceled Socket.ConnectAsync call can return successfully but the Socket is closed because
            // of the cancellation. See https://github.com/dotnet/runtime/issues/75889.
            cancellationToken.ThrowIfCancellationRequested();

            if (_authenticationOptions is not null)
            {
                _sslStream = new SslStream(new NetworkStream(Socket, false), false);

                await _sslStream.AuthenticateAsClientAsync(
                    _authenticationOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            return new TransportConnectionInformation(
                localNetworkAddress: Socket.LocalEndPoint!,
                remoteNetworkAddress: Socket.RemoteEndPoint!,
                _sslStream?.RemoteCertificate);
        }
        catch (IOException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
    }
}

internal class TcpServerConnection : TcpConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private SslStream? _sslStream;

    internal TcpServerConnection(
        Socket socket,
        SslServerAuthenticationOptions? authenticationOptions,
        MemoryPool<byte> pool,
        int minimumSegmentSize)
        : base(pool, minimumSegmentSize)
    {
        Socket = socket;
        _authenticationOptions = authenticationOptions;
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        try
        {
            if (_authenticationOptions is not null)
            {
                // This can only be created with a connected socket.
                _sslStream = new SslStream(new NetworkStream(Socket, false), false);
                await _sslStream.AuthenticateAsServerAsync(
                    _authenticationOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            return new TransportConnectionInformation(
                localNetworkAddress: Socket.LocalEndPoint!,
                remoteNetworkAddress: Socket.RemoteEndPoint!,
                _sslStream?.RemoteCertificate);
        }
        catch (IOException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
    }
}
