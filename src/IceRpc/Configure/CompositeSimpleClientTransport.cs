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
        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The TCP client options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeClientTransport<ISimpleNetworkConnection> UseSsl(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport,
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
        public static CompositeClientTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport) =>
            clientTransport.UseTcp(new TcpClientOptions());

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The TCP client options.</param>
        /// <returns>The transport being configured.</returns>
        public static CompositeClientTransport<ISimpleNetworkConnection> UseTcp(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport,
            TcpClientOptions options) =>
            clientTransport.Add(TransportNames.Tcp, new TcpClientTransport(options));

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <returns>The client transport being configured.</returns>
        public static CompositeClientTransport<ISimpleNetworkConnection> UseUdp(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport) =>
            clientTransport.UseUdp(new UdpClientOptions());

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The client transport being configured.</returns>
        public static CompositeClientTransport<ISimpleNetworkConnection> UseUdp(
            this CompositeClientTransport<ISimpleNetworkConnection> clientTransport,
            UdpClientOptions options) =>
            clientTransport.Add(TransportNames.Udp, new UdpClientTransport(options));
    }
}
