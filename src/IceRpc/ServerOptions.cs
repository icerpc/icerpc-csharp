// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a property bag used to configure a <see cref="Server" />.</summary>
public sealed record class ServerOptions
{
    /// <summary>Gets or sets the connection options for server connections.</summary>
    /// <value>The connection options. Defaults to a default constructed <see cref="ConnectionOptions" />.</value>
    public ConnectionOptions ConnectionOptions { get; set; } = new();

    /// <summary>Gets or sets the connection establishment timeout for connections accepted by the server.</summary>
    /// <value>Defaults to <c>10</c> seconds.</value>
    public TimeSpan ConnectTimeout
    {
        get => _connectTimeout;
        set => _connectTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the maximum number of accepted server connections. Once the maximum number of connections
    /// has been reached, the server will refuse any new connections.</summary>
    /// <value>The maximum number of connections. Defaults to <c>0</c>, meaning unlimited.</value>
    public int MaxConnections { get; set; }

    /// <summary>Gets or sets the maximum number of server connections waiting for connection establishment to complete.
    /// Once the maximum number of pending connections has been reached, the server will stop accepting new connections
    /// to ensure that the transport rejects new connections once its connection backlog is full.</summary>
    /// <value>The maximum number of connection waiting for connection establishment to complete. Defaults to
    /// <c>100</c>.</value>
    public int MaxPendingConnections { get; set; } = 100;

    /// <summary>Gets or sets the server's address. The server address host is usually an IP address, and it cannot be a
    /// DNS name.</summary>
    /// <value>The <see cref="ServerAddress" /> of this <see cref="Server" />. Defaults to a <see cref="ServerAddress"
    /// /> constructed with <see cref="Protocol.IceRpc" />.</value>
    public ServerAddress ServerAddress { get; set; } = new(Protocol.IceRpc);

    /// <summary>Gets or sets the SSL server authentication options.</summary>
    /// <value>The SSL server authentication options. When not <see langword="null" />, the server will accept only
    /// secure connections.</value>
    public SslServerAuthenticationOptions? ServerAuthenticationOptions { get; set; }

    /// <summary>Gets or sets the shutdown timeout. This timeout is used when gracefully shutting down a connection
    /// managed by the server.</summary>
    /// <value>Defaults to <c>10</c> seconds.</value>
    public TimeSpan ShutdownTimeout
    {
        get => _shutdownTimeout;
        set => _shutdownTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ShutdownTimeout)}", nameof(value));
    }

    private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);
}
