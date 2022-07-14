// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal;

internal abstract class TcpTransportConnection : ISingleStreamTransportConnection
{
    internal abstract Socket Socket { get; }

    internal abstract SslStream? SslStream { get; }

    private protected volatile bool _isDisposed;

    // The MaxDataSize of the SSL implementation.
    private const int MaxSslDataSize = 16 * 1024;

    private readonly List<ArraySegment<byte>> _segments = new();

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;

        if (SslStream is SslStream sslStream)
        {
            sslStream.Dispose();
        }

        Socket.Close(0);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
    {
        if (buffer.Length == 0)
        {
            throw new ArgumentException($"empty {nameof(buffer)}");
        }

        int received;
        try
        {
            if (SslStream is not null)
            {
                received = await SslStream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            }
            else
            {
                received = await Socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // a disposed Socket throws SocketException instead of ObjectDisposedException
        catch when (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(TcpTransportConnection)}");
        }
        catch (Exception exception)
        {
            throw exception.ToTransportException();
        }

        return received;
    }

    public async Task ShutdownAsync(CancellationToken cancel)
    {
        try
        {
            if (SslStream is SslStream sslStream)
            {
                await sslStream.ShutdownAsync().ConfigureAwait(false);
            }

            // Shutdown the socket send side to send a TCP FIN packet. We don't close the read side because we want
            // to be notified when the peer shuts down it's side of the socket (through the ReceiveAsync call).
            Socket.Shutdown(SocketShutdown.Send);
        }
        catch
        {
            // Ignore, the socket might already be disposed or it might not be connected.
        }
    }

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
    {
        Debug.Assert(buffers.Count > 0);

        try
        {
            if (SslStream is SslStream sslStream)
            {
                if (buffers.Count == 1)
                {
                    await sslStream.WriteAsync(buffers[0], cancel).ConfigureAwait(false);
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
                        using IMemoryOwner<byte> writeBufferOwner = MemoryPool<byte>.Shared.Rent(writeBufferSize);
                        Memory<byte> writeBuffer = writeBufferOwner.Memory[0..writeBufferSize];
                        int offset = 0;
                        for (int i = 0; i < index; ++i)
                        {
                            ReadOnlyMemory<byte> buffer = buffers[i];
                            buffer.CopyTo(writeBuffer[offset..]);
                            offset += buffer.Length;
                        }

                        // Send the "coalesced" initial buffers
                        await sslStream.WriteAsync(writeBuffer, cancel).ConfigureAwait(false);
                    }

                    // Send the remaining buffers one by one
                    for (int i = index; i < buffers.Count; ++i)
                    {
                        await sslStream.WriteAsync(buffers[i], cancel).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                if (buffers.Count == 1)
                {
                    await Socket.SendAsync(buffers[0], SocketFlags.None, cancel).ConfigureAwait(false);
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
                                $"{nameof(buffers)} are not backed by arrays",
                                nameof(buffers));
                        }
                    }
                    await Socket.SendAsync(_segments, SocketFlags.None).WaitAsync(cancel).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // a disposed Socket throws SocketException instead of ObjectDisposedException
        catch when (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(TcpTransportConnection)}");
        }
        catch (Exception exception)
        {
            throw exception.ToTransportException();
        }
    }
}

internal class TcpClientTransportConnection : TcpTransportConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly EndPoint _addr;
    private readonly SslClientAuthenticationOptions? _authenticationOptions;

    private bool _connected;

    private SslStream? _sslStream;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        Debug.Assert(!_connected);
        _connected = true;

        try
        {
            Debug.Assert(Socket is not null);

            // Connect to the peer.
            await Socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

            if (_authenticationOptions is not null)
            {
                // This can only be created with a connected socket.
                _sslStream = new SslStream(new NetworkStream(Socket, false), false);
                await _sslStream.AuthenticateAsClientAsync(_authenticationOptions, cancel).ConfigureAwait(false);
            }

            return new TransportConnectionInformation(
                localNetworkAddress: Socket.LocalEndPoint!,
                remoteNetworkAddress: Socket.RemoteEndPoint!,
                _sslStream?.RemoteCertificate);
        }
        catch (AuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // a disposed Socket throws SocketException instead of ObjectDisposedException
        catch when (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(TcpTransportConnection)}");
        }
        catch (Exception exception)
        {
            throw exception.ToConnectFailedException();
        }
    }

    internal TcpClientTransportConnection(
        string host,
        ushort port,
        SslClientAuthenticationOptions? authenticationOptions,
        TcpClientTransportOptions options)
    {
        _addr = IPAddress.TryParse(host, out IPAddress? ipAddress) ?
            new IPEndPoint(ipAddress, port) :
            new DnsEndPoint(host, port);

        _authenticationOptions = authenticationOptions;

        // When using IPv6 address family we use the socket constructor without AddressFamiliy parameter to ensure
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
        catch (SocketException ex)
        {
            Socket.Dispose();
            throw new TransportException(ex);
        }
    }
}

internal class TcpServerTransportConnection : TcpTransportConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private bool _connected;
    private readonly Endpoint _endpoint;
    private SslStream? _sslStream;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        Debug.Assert(!_connected);
        _connected = true;

        try
        {
            if (_authenticationOptions is not null)
            {
                // This can only be created with a connected socket.
                _sslStream = new SslStream(new NetworkStream(Socket, false), false);
                await _sslStream.AuthenticateAsServerAsync(_authenticationOptions, cancel).ConfigureAwait(false);
            }

            var ipEndPoint = (IPEndPoint)Socket.RemoteEndPoint!;

            return new TransportConnectionInformation(
                localNetworkAddress: Socket.LocalEndPoint!,
                remoteNetworkAddress: Socket.RemoteEndPoint!,
                _sslStream?.RemoteCertificate);
        }
        catch (AuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // a disposed Socket throws SocketException instead of ObjectDisposedException
        catch when (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(TcpTransportConnection)}");
        }
        catch (Exception exception)
        {
            throw exception.ToConnectFailedException();
        }
    }

    internal TcpServerTransportConnection(
        Socket socket,
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions)
    {
        Socket = socket;
        _authenticationOptions = authenticationOptions;
        _endpoint = endpoint;
    }
}
