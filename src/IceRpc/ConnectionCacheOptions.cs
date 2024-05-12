// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a property bag used to configure a <see cref="ConnectionCache" />.</summary>
public record class ConnectionCacheOptions
{
    /// <summary>Gets or sets the SSL client authentication options.</summary>
    /// <value>The SSL client authentication options. When not <see langword="null" />,
    /// <see cref="ClientConnection.ConnectAsync(CancellationToken)" /> will either establish a secure connection or
    /// fail.</value>
    public SslClientAuthenticationOptions? ClientAuthenticationOptions { get; set; }

    /// <summary>Gets or sets the connection options used for connections created by the connection cache.</summary>
    /// <value>The connection options. Defaults to a default constructed <see cref="ConnectionOptions" />.</value>
    public ConnectionOptions ConnectionOptions { get; set; } = new();

    /// <summary>Gets or sets the connection establishment timeout for connections created by the connection cache.
    /// </summary>
    /// <value>The connection establishment timeout. Defaults to <c>10</c> seconds.</value>
    public TimeSpan ConnectTimeout
    {
        get => _connectTimeout;
        set => _connectTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}.", nameof(value));
    }

    /// <summary>Gets or sets a value indicating whether or not the connection cache prefers an active connection over
    /// creating a new one.</summary>
    /// <value>When <see langword="true" />, the connection cache first checks the server addresses of the target
    /// service address: if any matches an active connection it manages, it sends the request over this connection. It
    /// does not check connections being connected. When <see langword="false" />, the connection cache does not prefer
    /// existing connections.</value>
    public bool PreferExistingConnection { get; set; } = true;

    /// <summary>Gets or sets the shutdown timeout. This timeout is used when gracefully shutting down a connection
    /// managed by the connection cache.</summary>
    /// <value>This shutdown timeout. Defaults to <c>10</c> seconds.</value>
    public TimeSpan ShutdownTimeout
    {
        get => _shutdownTimeout;
        set => _shutdownTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ShutdownTimeout)}.", nameof(value));
    }

    private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);
}
