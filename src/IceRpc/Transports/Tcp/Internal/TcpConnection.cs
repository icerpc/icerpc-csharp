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
    private readonly List<ArraySegment<byte>> _segments = new();
    private readonly IMemoryOwner<byte>? _writeBufferOwner;

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
        _writeBufferOwner?.Dispose();
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

    public ValueTask WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return PerformWriteAsync();

        async ValueTask PerformWriteAsync()
        {
            try
            {
                if (SslStream is SslStream sslStream)
                {
                    if (buffer.IsSingleSegment)
                    {
                        await sslStream.WriteAsync(buffer.First, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Coalesce leading segments up to _maxSslBufferSize. We don't coalesce trailing segments as we
                        // assume these segments are large enough.
                        int leadingSize = 0;
                        int leadingSegmentCount = 0;
                        foreach (ReadOnlyMemory<byte> memory in buffer)
                        {
                            if (leadingSize + memory.Length <= _maxSslBufferSize)
                            {
                                leadingSize += memory.Length;
                                leadingSegmentCount++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (leadingSegmentCount > 1)
                        {
                            ReadOnlySequence<byte> leading = buffer.Slice(0, leadingSize);
                            buffer = buffer.Slice(leadingSize); // buffer can become empty

                            Debug.Assert(_writeBufferOwner is not null);
                            Memory<byte> writeBuffer = _writeBufferOwner.Memory[0..leadingSize];
                            leading.CopyTo(writeBuffer.Span);

                            // Send the "coalesced" leading segments
                            await sslStream.WriteAsync(writeBuffer, cancellationToken).ConfigureAwait(false);
                        }
                        // else no need to coalesce (copy) a single segment

                        // Send the remaining segments one by one
                        if (buffer.IsEmpty)
                        {
                            // done
                        }
                        else if (buffer.IsSingleSegment)
                        {
                            await sslStream.WriteAsync(buffer.First, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            foreach (ReadOnlyMemory<byte> memory in buffer)
                            {
                                await sslStream.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                }
                else
                {
                    if (buffer.IsSingleSegment)
                    {
                        _ = await Socket.SendAsync(buffer.First, SocketFlags.None, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        _segments.Clear();
                        foreach (ReadOnlyMemory<byte> memory in buffer)
                        {
                            if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
                            {
                                _segments.Add(segment);
                            }
                            else
                            {
                                throw new ArgumentException(
                                    $"The {nameof(buffer)} must be backed by arrays.",
                                    nameof(buffer));
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

    private protected TcpConnection(IMemoryOwner<byte>? memoryOwner)
    {
        _writeBufferOwner = memoryOwner;
        // When coalescing leading buffers in WriteAsync (SSL only), the upper size limit is the lesser of the size of
        // the buffer we rented from the memory pool (typically 4K) and MaxSslDataSize (16K).
        _maxSslBufferSize = Math.Min(memoryOwner?.Memory.Length ?? 0, MaxSslDataSize);
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
        : base(authenticationOptions is not null ? pool.Rent(minimumSegmentSize) : null)
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
        : base(authenticationOptions is not null ? pool.Rent(minimumSegmentSize) : null)
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
