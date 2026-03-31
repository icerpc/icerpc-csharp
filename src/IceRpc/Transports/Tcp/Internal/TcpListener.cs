// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Tcp.Internal;

/// <summary>The listener implementation for the TCP transport.</summary>
internal sealed class TcpListener : IListener<IDuplexConnection>
{
    public TransportAddress TransportAddress { get; }

    private readonly SslServerAuthenticationOptions? _serverAuthenticationOptions;
    private readonly int _minSegmentSize;
    private readonly MemoryPool<byte> _pool;
    private readonly Socket _socket;

    // Set to 1 when the listener is disposed.
    private volatile int _disposed;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        try
        {
            Socket acceptedSocket = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);

            var tcpConnection = new TcpServerConnection(
                acceptedSocket,
                _serverAuthenticationOptions,
                _pool,
                _minSegmentSize);
            return (tcpConnection, acceptedSocket.RemoteEndPoint!);
        }
        catch (SocketException exception)
        {
            if (exception.SocketErrorCode == SocketError.OperationAborted)
            {
                ObjectDisposedException.ThrowIf(_disposed == 1, this);
            }
            throw exception.ToIceRpcException();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _socket.Dispose();
        }
        return default;
    }

    internal TcpListener(
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions,
        TcpServerTransportOptions tcpOptions)
    {
        if (!IPAddress.TryParse(transportAddress.Host, out IPAddress? ipAddress))
        {
            throw new ArgumentException(
                $"Listening on the DNS name '{transportAddress.Host}' is not allowed; an IP address is required.",
                nameof(transportAddress));
        }

        _serverAuthenticationOptions = serverAuthenticationOptions;
        _minSegmentSize = options.MinSegmentSize;
        _pool = options.Pool;

        var address = new IPEndPoint(ipAddress, transportAddress.Port);

        // When using IPv6 address family we use the socket constructor without AddressFamily parameter to ensure
        // dual-mode socket are used in platforms that support them.
        _socket = ipAddress.AddressFamily == AddressFamily.InterNetwork ?
            new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) :
            new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _socket.ExclusiveAddressUse = true;
            _socket.Configure(tcpOptions);
            _socket.Bind(address);
            address = (IPEndPoint)_socket.LocalEndPoint!;
            _socket.Listen(tcpOptions.ListenBacklog);
        }
        catch (SocketException exception)
        {
            _socket.Dispose();
            throw exception.ToIceRpcException();
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        TransportAddress = transportAddress with { Port = (ushort)address.Port };
    }
}
