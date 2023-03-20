// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports;

/// <summary>The base options class for Quic transports.</summary>
public record class QuicTransportOptions
{
    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted.</summary>
    /// <value>The idle timeout. Defaults to <see cref="TimeSpan.Zero" /> meaning that the underlying Quic
    /// implementation default will be used.</value>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.Zero;
}

/// <summary>The options class for configuring <see cref="QuicClientTransport"/>.</summary>
public sealed record class QuicClientTransportOptions : QuicTransportOptions
{
    /// <summary>Gets or sets the address and port represented by a .NET IPEndPoint to use for a client Quic connection.
    /// If specified the client Quic connection will bind to this address and port before connection establishment.
    /// </summary>
    /// <value>The address and port to bind to. Defaults to <see langword="null" />.</value>
    public IPEndPoint? LocalNetworkAddress { get; set; }
}

/// <summary>The options class for configuring <see cref="QuicServerTransport"/>.</summary>
public sealed record class QuicServerTransportOptions : QuicTransportOptions
{
    /// <summary>Gets or sets the length of the server listen queue for accepting new connections. If a new connection
    /// request arrives and the queue is full, the client connection establishment will fail with a <see
    /// cref="IceRpcException"/> and the <see cref="IceRpcError.ConnectionRefused"/> error code.</summary>
    /// <value>The server listen backlog size. Defaults to <c>511</c>.</value>
    public int ListenBacklog
    {
        get => _listenBacklog;
        set => _listenBacklog = value > 0 ? value :
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Invalid value '{value}' for {nameof(ListenBacklog)}, it cannot be less than 1.");
    }

    private int _listenBacklog = 511;
}
