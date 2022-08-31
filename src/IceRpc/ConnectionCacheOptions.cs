// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc;

/// <summary>A property bag used to configure a <see cref="ConnectionCache"/>.</summary>
public record class ConnectionCacheOptions
{
    /// <summary>Gets or sets the SSL client authentication options.</summary>
    /// <value>The SSL client authentication options.</value>
    public SslClientAuthenticationOptions? ClientAuthenticationOptions { get; set; }

    /// <summary>Gets or sets the connection options used for connections created by the connection cache.</summary>
    public ClientConnectionOptions ConnectionOptions { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether or not the connection cache prefers an active connection over
    /// creating a new one.</summary>
    /// <value>When <c>true</c>, the connection cache first checks the server addresses of the target service address:
    /// if any matches an active connection it manages, it sends the request over this connection. It does not check
    /// connections being connected. When <c>false</c>, the connection cache does not prefer existing connections.
    /// </value>
    public bool PreferExistingConnection { get; set; } = true;

    /// <summary>Gets or sets the maximum number of active connections that the connection cache will cache.
    /// Once the maximum number connections has been reached, new connections will wait until an active connection
    /// is closed.</summary>
    public int MaxConnections { get; set; } = 100;
}
