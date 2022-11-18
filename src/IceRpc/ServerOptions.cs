// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc;

/// <summary>A property bag used to configure a <see cref="Server" />.</summary>
public sealed record class ServerOptions
{
    /// <summary>Gets or sets the connection options for server connections.</summary>
    public ConnectionOptions ConnectionOptions { get; set; } = new();

    /// <summary>Gets or sets the maximum number of accepted server connections. Once the maximum number of connections
    /// has been reached, the server will refuse any new connections. The default value <c>0</c> means unlimited.
    /// </summary>
    public int MaxConnections { get; set; }

    /// <summary>Gets or sets the maximum number of server connections waiting for connection establishment to complete.
    /// Once the maximum number of pending connections has been reached, the server will stop accepting new connections
    /// to ensure that the transport rejects new connections once its connection backlog is full. The default value is
    /// <c>100</c>.
    /// </summary>
    public int MaxPendingConnections { get; set; } = 100;

    /// <summary>Gets or sets the server's address. The server address host is usually an IP address, and it cannot be a
    /// DNS name.</summary>
    public ServerAddress ServerAddress { get; set; } = new(Protocol.IceRpc);

    /// <summary>Gets or sets the SSL server authentication options.</summary>
    /// <value>The SSL server authentication options. When not null, the server will accept only secure connections.
    /// </value>
    public SslServerAuthenticationOptions? ServerAuthenticationOptions { get; set; }
}
