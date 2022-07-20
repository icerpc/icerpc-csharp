// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a server <see cref="IDuplexConnection"/>.</summary>
public sealed record class DuplexServerConnectionOptions : DuplexConnectionOptions
{
    /// <summary>Gets or sets the SSL server authentication options.</summary>
    /// <value>The SSL server authentication options. When not null, <see
    /// cref="IDuplexConnection.ConnectAsync(CancellationToken)"/> will either establish a secure connection or
    /// fail.</value>
    public SslServerAuthenticationOptions? ServerAuthenticationOptions { get; set; }
}
