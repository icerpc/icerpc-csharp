// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports;

/// <summary>The base options class for TCP transports.</summary>
public record class TcpTransportOptions
{
    /// <summary>Gets or sets the socket receive buffer size in bytes.</summary>
    /// <value>The receive buffer size in bytes. It can't be less than <c>1</c> KB. <see langword="null" /> means use
    /// the operating system default. Defaults to <see langword="null" />.</value>
    public int? ReceiveBufferSize
    {
        get => _receiveBufferSize;
        set => _receiveBufferSize = value is null || value >= 1024 ? value :
            throw new ArgumentException(
                $"The {nameof(ReceiveBufferSize)} value cannot be less than 1KB.",
                nameof(value));
    }

    /// <summary>Gets or sets the socket send buffer size in bytes.</summary>
    /// <value>The send buffer size in bytes. It can't be less than <c>1</c> KB. <see langword="null" /> means use the
    /// OS default. Defaults to <see langword="null" />.
    /// </value>
    public int? SendBufferSize
    {
        get => _sendBufferSize;
        set => _sendBufferSize = value is null || value >= 1024 ? value :
            throw new ArgumentException(
                $"The {nameof(SendBufferSize)} value cannot be less than 1KB.",
                nameof(value));
    }

    private int? _receiveBufferSize;
    private int? _sendBufferSize;
}

/// <summary>The options class for configuring <see cref="TcpClientTransport" />.</summary>
public sealed record class TcpClientTransportOptions : TcpTransportOptions
{
    /// <summary>Gets or sets the address and port represented by a .NET IPEndPoint to use for a client
    /// socket. If specified the client socket will bind to this address and port before connection establishment.
    /// </summary>
    /// <value>The address and port to bind the socket to. Defaults to <see langword="null" />.</value>
    public IPEndPoint? LocalNetworkAddress { get; set; }
}

/// <summary>The options class for configuring <see cref="TcpServerTransport" />.</summary>
public sealed record class TcpServerTransportOptions : TcpTransportOptions
{
    /// <summary>Gets or sets the length of the server socket queue for accepting new connections. If a new connection
    /// request arrives and the queue is full, the client connection establishment will fail with a <see
    /// cref="IceRpcException" /> and the <see cref="IceRpcError.ConnectionRefused" /> error code.</summary>
    /// <value>The server socket backlog size. Defaults to <c>511</c>.</value>
    public int ListenBacklog
    {
        get => _listenBacklog;
        set => _listenBacklog = value > 0 ? value :
            throw new ArgumentException($"The {nameof(ListenBacklog)} value cannot be less than 1", nameof(value));
    }

    private int _listenBacklog = 511;
}
