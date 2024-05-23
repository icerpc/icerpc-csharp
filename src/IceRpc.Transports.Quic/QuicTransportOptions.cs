// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports.Quic;

/// <summary>The base options class for QUIC transports.</summary>
public record class QuicTransportOptions
{
    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted.</summary>
    /// <value>The idle timeout. Defaults to 30 seconds. <see cref="TimeSpan.Zero" /> means "use the default value
    /// provided by the underlying implementation".</value>
    /// <remarks>The idle timeout is negotiated by QUIC during connection establishment. The actual idle timeout value
    /// for a connection can be lower than the value you've set here.</remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>The options class for configuring <see cref="QuicClientTransport"/>.</summary>
public sealed record class QuicClientTransportOptions : QuicTransportOptions
{
    /// <summary>Gets or sets the address and port represented by a .NET <see cref="IPEndPoint"/> to use for a client
    /// QUIC connection. If specified the client QUIC connection will bind to this address and port before connection
    /// establishment.</summary>
    /// <value>The address and port to bind to. Defaults to <see langword="null" />.</value>
    public IPEndPoint? LocalNetworkAddress { get; set; }

    /// <summary>Gets or sets the interval at which the client sends QUIC PING frames to the server to keep the
    /// connection alive.</summary>
    /// <value>The keep-alive interval. Defaults to 15 seconds. <see cref="TimeSpan.Zero" /> means: use the default
    /// value provided by the underlying implementation. <see cref="Timeout.InfiniteTimeSpan" /> means: never send
    /// PING frames.</value>
    /// <remarks>Unlike the idle timeout, the keep-alive interval is not negotiated during connection establishment. We
    /// recommend setting this interval at half the negotiated idle timeout value to keep a healthy connection alive
    /// forever - really until it's closed due to inactivity at the RPC level via the
    /// <see cref="ConnectionOptions.InactivityTimeout" />.
    /// This option is a stop-gap: ideally the .NET QUIC implementation would provide a way to automatically send PING
    /// frames to keep the connection alive based on the negotiated idle timeout. See
    /// <see href="https://github.com/microsoft/msquic/issues/3880" />.</remarks>
    /// <remarks>This option is only implemented for .NET 9.0. With .NET 8.0, neither the client nor the server sends
    /// PING frames to keep the connection alive.</remarks>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
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
