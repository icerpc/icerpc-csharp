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

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The server transport being configured.</param>
        /// <returns>The server transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseUdp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.UseUdp(new UdpServerTransportOptions());

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The server transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseUdp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport,
            UdpServerTransportOptions options) =>
            serverTransport.Add(new UdpServerTransport(options));
    }
}
