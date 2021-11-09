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
        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseColoc(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.Add(TransportNames.Coloc, new ColocServerTransport());

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The TCP server options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseSsl(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpServerOptions options)
        {
            if (options.AuthenticationOptions == null)
            {
                throw new ArgumentException("AuthenticationOptions must be set for ssl transport", nameof(options));
            }
            return serverTransport.Add(TransportNames.Ssl, new TcpServerTransport(options));
        }

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.UseTcp(new TcpServerOptions());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The TCP server options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeServerTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpServerOptions options) =>
            serverTransport.Add(TransportNames.Tcp, new TcpServerTransport(options));

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
