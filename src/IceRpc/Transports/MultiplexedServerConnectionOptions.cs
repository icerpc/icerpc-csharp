// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a server <see cref="IMultiplexedConnection"/>.</summary>
public sealed record class MultiplexedServerConnectionOptions : MultiplexedConnectionOptions
{
    /// <summary>Gets or sets the SSL server authentication options.</summary>
    /// <value>The SSL server authentication options. When not null, <see
    /// cref="IMultiplexedConnection.ConnectAsync(CancellationToken)"/> will either establish a secure connection or
    /// fail.</value>
    public SslServerAuthenticationOptions? ServerAuthenticationOptions { get; set; }
}
