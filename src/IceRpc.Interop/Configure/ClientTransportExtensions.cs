// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>Extension methods for class <see cref="ClientTransport"/>.</summary>
    public static class ClientTransportExtensions
    {
        /// <summary>Adds the interop coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseInteropColoc(this ClientTransport clientTransport) =>
            clientTransport.UseInteropColoc(new());

        /// <summary>Adds the interop coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseInteropColoc(
            this ClientTransport clientTransport,
            MultiStreamOptions options) =>
            clientTransport.Add(TransportNames.Coloc, Protocol.Ice1, new ColocClientTransport(options));

        /// <summary>Adds the interop ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseInteropSsl(
            this ClientTransport clientTransport,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.Add(TransportNames.Ssl, Protocol.Ice1, new TcpClientTransport(authenticationOptions));

        /// <summary>Adds the interop ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseInteropSsl(
            this ClientTransport clientTransport,
            TcpOptions tcpOptions,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.Add(TransportNames.Ssl,
                                Protocol.Ice1,
                                new TcpClientTransport(tcpOptions, new(), authenticationOptions));

        /// <summary>Adds the interop tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseInteropTcp(this ClientTransport clientTransport) =>
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice1, new TcpClientTransport());

        /// <summary>Adds the interop tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseInteropTcp(this ClientTransport clientTransport, TcpOptions tcpOptions) =>
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice1, new TcpClientTransport(tcpOptions, new(), null));

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport UseInteropUdp(this ClientTransport clientTransport) =>
            clientTransport.UseInteropUdp(new UdpOptions());

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <param name="udpOptions">The UDP transport options.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport UseInteropUdp(this ClientTransport clientTransport, UdpOptions udpOptions) =>
            clientTransport.Add(TransportNames.Udp, Protocol.Ice1, new UdpClientTransport(udpOptions));
    }
}
