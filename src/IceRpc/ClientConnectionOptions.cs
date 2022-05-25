// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure a <see cref="ClientConnection"/>.</summary>
    public sealed record class ClientConnectionOptions : ConnectionOptions
    {
        /// <summary>Gets or sets the SSL client authentication options.</summary>
        /// <value>The SSL client authentication options. When not null,
        /// <see cref="Connection.ConnectAsync(CancellationToken)"/> will either establish a secure connection or fail.
        /// </value>
        public SslClientAuthenticationOptions? ClientAuthenticationOptions { get; set; }

        /// <summary>Specifies if the connection can be resumed after being closed.</summary>
        /// <value>When <c>true</c>, the connection will be re-established by the next call to
        /// <see cref="Connection.ConnectAsync(CancellationToken)"/> or the next invocation. The
        /// <see cref="Connection.State"/> is always switched back to <see cref="ConnectionState.NotConnected"/> after
        /// the connection closure. When <c>false</c>, the <see cref="Connection.State"/> is
        /// <see cref="ConnectionState.Closed"/> once the connection is closed and the connection won't be resumed. The
        /// default value is <c>false</c>.</value>
        public bool IsResumable { get; set; }

        /// <summary>Gets or sets the connection's remote endpoint. For a client connection this is the connection's
        /// remote endpoint, for a server connection it's the server's endpoint.</summary>
        public Endpoint? RemoteEndpoint { get; set; }
    }
}
