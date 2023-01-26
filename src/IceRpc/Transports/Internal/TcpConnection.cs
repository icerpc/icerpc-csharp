// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace IceRpc.Transports.Internal;

internal abstract class TcpConnection : IDuplexConnection
{
    public ServerAddress ServerAddress { get; }

    internal abstract Socket Socket { get; }

    internal abstract SslStream? SslStream { get; }

    private protected volatile bool _isDisposed;

    // The MaxDataSize of the SSL implementation.
    private const int MaxSslDataSize = 16 * 1024;

    private bool _isShutdown;
    private readonly int _minSegmentSize;
    private readonly MemoryPool<byte> _pool;
    private readonly List<ArraySegment<byte>> _segments = new();

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

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

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (buffer.Length == 0)
        {
            throw new ArgumentException($"The {nameof(buffer)} cannot be empty.", nameof(buffer));
        }

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

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (SslStream is SslStream sslStream)
            {
                await sslStream.ShutdownAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            // Shutdown the socket send side to send a TCP FIN packet. We don't close the read side because we want
            // to be notified when the peer shuts down it's side of the socket (through the ReceiveAsync call).
            Socket.Shutdown(SocketShutdown.Send);
            // If shutdown is successful mark the connection as shutdown to ensure Dispose won't reset the TCP
            // connection.
            _isShutdown = true;
        }
        catch
        {
            // Ignore, the socket might already be disposed or it might not be connected.
        }
    }

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        Debug.Assert(buffers.Count > 0);

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
                    // Coalesce leading small buffers up to MaxSslDataSize. We assume buffers later on are
                    // large enough and don't need coalescing.
                    int index = 0;
                    int writeBufferSize = 0;
                    do
                    {
                        ReadOnlyMemory<byte> buffer = buffers[index];
                        if (writeBufferSize + buffer.Length < MaxSslDataSize)
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
                        using IMemoryOwner<byte> writeBufferOwner =
                            _pool.Rent(Math.Max(_minSegmentSize, writeBufferSize));
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
                    _ = await Socket.SendAsync(buffers[0], SocketFlags.None, cancellationToken).ConfigureAwait(false);
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
                        // Abort the connection and wait for sendTask to complete
                        Socket.Close(0);

                        try
                        {
                            await sendTask.ConfigureAwait(false);
                        }
                        catch
                        {
                            // expected, most likely OperationAborted
                        }
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

    private protected TcpConnection(
        ServerAddress serverAddress,
        MemoryPool<byte> pool,
        int minimumSegmentSize)
    {
        ServerAddress = serverAddress;
        _pool = pool;
        _minSegmentSize = minimumSegmentSize;
    }
}

internal class TcpClientConnection : TcpConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly EndPoint _addr;
    private readonly SslClientAuthenticationOptions? _authenticationOptions;

    private SslStream? _sslStream;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

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

    internal TcpClientConnection(
        ServerAddress serverAddress,
        SslClientAuthenticationOptions? authenticationOptions,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        TcpClientTransportOptions options)
        : base(serverAddress, pool, minimumSegmentSize)
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
}

internal class TcpServerConnection : TcpConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private SslStream? _sslStream;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

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

    internal TcpServerConnection(
        ServerAddress serverAddress,
        Socket socket,
        SslServerAuthenticationOptions? authenticationOptions,
        MemoryPool<byte> pool,
        int minimumSegmentSize)
        : base(serverAddress, pool, minimumSegmentSize)
    {
        Socket = socket;
        _authenticationOptions = authenticationOptions;
    }
}
