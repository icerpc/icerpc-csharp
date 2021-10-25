// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A composite server transport that creates simple network connections.</summary>
    public class SimpleServerTransport : ServerTransport<ISimpleNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="ServerTransport{ISimpleNetworkConnection}"/>.</summary>
    public static class SimpleServerTransportExtensions
    {
        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseColoc(
            this ServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.Add(TransportNames.Coloc, new ColocServerTransport());

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseSsl(
            this ServerTransport<ISimpleNetworkConnection> serverTransport,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.UseSsl(new TcpOptions(), authenticationOptions);

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseSsl(
            this ServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpOptions tcpOptions,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.Add(TransportNames.Ssl,
                                new TcpServerTransport(tcpOptions, authenticationOptions));

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseTcp(
            this ServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.UseTcp(new TcpOptions());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseTcp(
            this ServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpOptions tcpOptions) =>
            serverTransport.Add(TransportNames.Tcp, new TcpServerTransport(tcpOptions, null));

        /// <summary>Adds the tcp server transport with ssl support to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseTcp(
            this ServerTransport<ISimpleNetworkConnection> serverTransport,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.UseTcp(new TcpOptions(), authenticationOptions);

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseTcp(
            this ServerTransport<ISimpleNetworkConnection> serverTransport,
            TcpOptions tcpOptions,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.Add(TransportNames.Tcp, new TcpServerTransport(tcpOptions, authenticationOptions));

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The server transport being configured.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseUdp(
            this ServerTransport<ISimpleNetworkConnection> serverTransport) =>
            serverTransport.UseUdp(new UdpOptions());

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport<ISimpleNetworkConnection> UseUdp(
            this ServerTransport<ISimpleNetworkConnection> serverTransport, UdpOptions options) =>
            serverTransport.Add(TransportNames.Udp, new UdpServerTransport(options));
    }
}
