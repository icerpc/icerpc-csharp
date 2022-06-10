// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc
{
    /// <summary>A property bag used to configure a <see cref="ClientConnection"/>.</summary>
    public sealed record class ClientConnectionOptions : ConnectionOptions
    {
        /// <summary>Gets or sets the SSL client authentication options.</summary>
        /// <value>The SSL client authentication options. When not null,
        /// <see cref="ClientConnection.ConnectAsync(CancellationToken)"/> will either establish a secure connection or
        /// fail.</value>
        public SslClientAuthenticationOptions? ClientAuthenticationOptions { get; set; }

        /// <summary>Gets or sets the connection's remote endpoint. For a client connection this is the connection's
        /// remote endpoint, for a server connection it's the server's endpoint.</summary>
        public Endpoint? RemoteEndpoint { get; set; }
    }
}
