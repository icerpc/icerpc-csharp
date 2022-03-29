// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport that creates simple network connections.</summary>
    public class CompositeSimpleClientTransport : CompositeClientTransport<ISimpleNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="CompositeClientTransport{ISimpleNetworkConnection}"/>.</summary>
    public static class CompositeSimpleClientTransportExtensions
    {
        /// <summary>Adds the tcp and ssl client transports to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeClientTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport) =>
            clientTransport.UseTcp(new TcpClientTransportOptions());

        /// <summary>Adds the tcp and ssl client transports to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The tcp client options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeClientTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport,
            TcpClientTransportOptions options)
        {
            var transport = new TcpClientTransport(options);
            return clientTransport.Add(transport).Add(TransportNames.Ssl, transport);
        }
    }
}
