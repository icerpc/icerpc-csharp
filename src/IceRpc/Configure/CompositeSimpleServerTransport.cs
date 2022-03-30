// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Configure
{
    /// <summary>A composite server transport that creates simple network connections.</summary>
    public class CompositeSimpleServerTransport : CompositeServerTransport<ISimpleNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="CompositeServerTransport{ISimpleNetworkConnection}"/>.</summary>
    public static class CompositeSimpleServerTransportExtensions
    {
        /// <summary>Adds the tcp and ssl server transports to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.UseTcp(new TcpServerTransportOptions());

        /// <summary>Adds the tcp and ssl server transports to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The tcp server options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpServerTransportOptions options)
        {
            var transport = new TcpServerTransport(options);
            return serverTransport.Add(transport).Add(TransportNames.Ssl, transport);
        }
    }
}
