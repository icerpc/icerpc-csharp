// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc;

/// <summary>A property bag used to configure a <see cref="ClientConnection" />.</summary>
public sealed record class ClientConnectionOptions : ConnectionOptions
{
    /// <summary>Gets or sets the SSL client authentication options.</summary>
    /// <value>The SSL client authentication options. When not null,
    /// <see cref="ClientConnection.ConnectAsync(CancellationToken)" /> will either establish a secure connection or
    /// fail.</value>
    public SslClientAuthenticationOptions? ClientAuthenticationOptions { get; set; }

    /// <summary>Gets or sets the connection establishment timeout.</summary>
    /// <value>Defaults to <c>10</c> seconds.</value>
    public TimeSpan ConnectTimeout
    {
        get => _connectTimeout;
        set => _connectTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the connection's server address.</summary>
    /// <value>The connections's server address. If null, the client connection construction will fail.</value>
    public ServerAddress? ServerAddress { get; set; }

    /// <summary>Gets or sets the shutdown timeout.</summary>
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
