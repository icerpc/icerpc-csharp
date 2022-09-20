// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

/// <summary>The listener implementation for the TCP transport.</summary>
internal sealed class TcpListener : IListener<IDuplexConnection>
{
    public ServerAddress ServerAddress { get; }

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private readonly int _minSegmentSize;
    private readonly MemoryPool<byte> _pool;
    private readonly Socket _socket;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                Socket acceptedSocket = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);

                var tcpConnection = new TcpServerConnection(
                    ServerAddress,
                    acceptedSocket,
                    _authenticationOptions,
                    _pool,
                    _minSegmentSize);
                return (tcpConnection, acceptedSocket.RemoteEndPoint!);
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.OperationAborted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw exception.ToTransportException();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException)
            {
                // If the connection was reset while in the backlog, retry.
            }
            catch (ObjectDisposedException)
            {
                // Dispose has been called.
                throw;
            }
            catch (Exception exception)
            {
                throw exception.ToTransportException();
            }
        }
    }

    public void Dispose() => _socket.Dispose();

    internal TcpListener(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions,
        TcpServerTransportOptions tcpOptions)
    {
        if (!IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress))
        {
            throw new NotSupportedException(
                $"serverAddress '{serverAddress}' cannot accept connections because it has a DNS name");
        }

        _authenticationOptions = serverAuthenticationOptions;
        _minSegmentSize = options.MinSegmentSize;
        _pool = options.Pool;

        if (_authenticationOptions is not null && _authenticationOptions.ApplicationProtocols is null)
        {
            // Set ApplicationProtocols to "ice" or "icerpc" in the common situation where the application does not
            // specify any application protocol. This way, a connection request that carries an ALPN protocol ID can
            // only succeed if this protocol ID is a match.
            _authenticationOptions.ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol(serverAddress.Protocol.Name)
            };
        }

        var address = new IPEndPoint(ipAddress, serverAddress.Port);

        // When using IPv6 address family we use the socket constructor without AddressFamily parameter to ensure
        // dual-mode socket are used in platforms that support them.
        _socket = ipAddress.AddressFamily == AddressFamily.InterNetwork ?
            new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) :
            new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _socket.ExclusiveAddressUse = true;

            if (tcpOptions.ReceiveBufferSize is int receiveSize)
            {
                _socket.ReceiveBufferSize = receiveSize;
            }
            if (tcpOptions.SendBufferSize is int sendSize)
            {
                _socket.SendBufferSize = sendSize;
            }

            _socket.Bind(address);
            address = (IPEndPoint)_socket.LocalEndPoint!;
            _socket.Listen(tcpOptions.ListenerBackLog);
        }
        catch (Exception exception)
        {
            _socket.Dispose();
            throw exception.ToTransportException();
        }

        ServerAddress = serverAddress with { Port = (ushort)address.Port };
    }
}
