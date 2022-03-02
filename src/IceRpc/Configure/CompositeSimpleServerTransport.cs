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
            serverTransport.UseTcp(new TcpServerOptions());

        /// <summary>Adds the tcp and ssl server transports to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The TCP server options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpServerOptions options)
        {
            var transport = new TcpServerTransport(options);
            return serverTransport.Add(TransportNames.Tcp, transport).Add(TransportNames.Ssl, transport);
        }

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The server transport being configured.</param>
        /// <returns>The server transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseUdp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.UseUdp(new UdpServerOptions());

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The server transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseUdp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport, UdpServerOptions options) =>
            serverTransport.Add(TransportNames.Udp, new UdpServerTransport(options));
    }
}
