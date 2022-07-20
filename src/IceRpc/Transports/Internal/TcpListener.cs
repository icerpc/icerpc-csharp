// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal;

/// <summary>The listener implementation for the TCP transport.</summary>
internal sealed class TcpListener : IDuplexListener
{
    public Endpoint Endpoint { get; }

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private readonly ILogger _logger;
    private int _minSegmentSize;
    private MemoryPool<byte> _pool;
    private readonly Socket _socket;

    public async Task<IDuplexConnection> AcceptAsync()
    {
        Socket acceptedSocket;
        try
        {
            acceptedSocket = await _socket.AcceptAsync().ConfigureAwait(false);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
        {
            // We translate this expected error into an ObjectDisposedException that the caller can safely catch and
            // ignore.
            throw new ObjectDisposedException(nameof(TcpListener), ex);
        }

#pragma warning disable CA2000 // the connection is disposed by the caller
        var serverConnection = new TcpServerDuplexConnection(
            Endpoint,
            acceptedSocket,
            _authenticationOptions,
            _pool,
            _minSegmentSize);
        if (_logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel))
        {
            return new LogTcpTransportConnectionDecorator(serverConnection, _logger);
        }
        else
        {
            return serverConnection;
        }
#pragma warning restore CA2000
    }

    public void Dispose() => _socket.Dispose();

    internal TcpListener(DuplexListenerOptions options, TcpServerTransportOptions tcpOptions)
    {
        if (!IPAddress.TryParse(options.Endpoint.Host, out IPAddress? ipAddress))
        {
            throw new NotSupportedException(
                $"endpoint '{options.Endpoint}' cannot accept connections because it has a DNS name");
        }

        _authenticationOptions = options.ServerConnectionOptions.ServerAuthenticationOptions;
        _logger = options.Logger;
        _minSegmentSize = options.ServerConnectionOptions.MinSegmentSize;
        _pool = options.ServerConnectionOptions.Pool;

        if (_authenticationOptions is not null)
        {
            // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN)
            _authenticationOptions = _authenticationOptions.Clone();
            _authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol(options.Endpoint.Protocol.Name)
            };
        }

        var address = new IPEndPoint(ipAddress, options.Endpoint.Port);
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
        catch (SocketException ex)
        {
            _socket.Dispose();
            throw ex.ToTransportException();
        }

        Endpoint = options.Endpoint with { Port = (ushort)address.Port };
    }
}
