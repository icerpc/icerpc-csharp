// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure a <see cref="Server"/>.</summary>
    public sealed record class ServerOptions
    {
        /// <summary>Gets or sets the SSL server authentication options.</summary>
        /// <value>The SSL server authentication options. When not null, the server will accept only secure connections.
        /// </value>
        public SslServerAuthenticationOptions? AuthenticationOptions { get; set; }

        /// <summary>Gets or set the options for server connections.</summary>
        public ConnectionOptions ConnectionOptions { get; set; } = new ();
        
        /// <summary>Gets or sets the server's endpoint. The endpoint's host is usually an IP address, and it
        /// cannot be a DNS name.</summary>
        public Endpoint Endpoint
        {
            get => _endpoint;
            set => _endpoint = value.Protocol.IsSupported ? value :
                throw new NotSupportedException($"cannot set endpoint with protocol '{value.Protocol}'");
        }

        private Endpoint _endpoint = new(Protocol.IceRpc);
    }
}
