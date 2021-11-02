// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport that creates simple network connections.</summary>
    public class SimpleClientTransport : ClientTransport<ISimpleNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="ClientTransport{ISimpleNetworkConnection}"/>.</summary>
    public static class SimpleClientTransportExtensions
    {
        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<ISimpleNetworkConnection> UseColoc(
            this ClientTransport<ISimpleNetworkConnection> clientTransport) =>
            clientTransport.Add(TransportNames.Coloc, new ColocClientTransport());

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The TCP client options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<ISimpleNetworkConnection> UseSsl(
            this ClientTransport<ISimpleNetworkConnection> clientTransport,
            TcpClientOptions options)
        {
            if (options.AuthenticationOptions == null)
            {
                throw new ArgumentException("AuthenticationOptions must be set for ssl transport", nameof(options));
            }

            return clientTransport.Add(TransportNames.Ssl, new TcpClientTransport(options));
        }

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<ISimpleNetworkConnection> UseTcp(
            this ClientTransport<ISimpleNetworkConnection> clientTransport) =>
            clientTransport.UseTcp(new TcpClientOptions());

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The TCP client options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<ISimpleNetworkConnection> UseTcp(
            this ClientTransport<ISimpleNetworkConnection> clientTransport,
            TcpClientOptions options) =>
            clientTransport.Add(TransportNames.Tcp, new TcpClientTransport(options));

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport<ISimpleNetworkConnection> UseUdp(
            this ClientTransport<ISimpleNetworkConnection> clientTransport) =>
            clientTransport.UseUdp(new UdpOptions());

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <param name="udpOptions">The UDP transport options.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport<ISimpleNetworkConnection> UseUdp(
            this ClientTransport<ISimpleNetworkConnection> clientTransport,
            UdpOptions udpOptions) =>
            clientTransport.Add(TransportNames.Udp, new UdpClientTransport(udpOptions));
    }
}
