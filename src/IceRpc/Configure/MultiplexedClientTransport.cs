// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport that creates multiplexed network connections.</summary>
    public class MultiplexedClientTransport : ClientTransport<IMultiplexedNetworkConnection>
    {
    }

    /// <summary>Extension methods for class <see cref="MultiplexedClientTransport"/>.</summary>
    public static class MultiplexedClientTransportExtensions
    {
        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseColoc(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport) =>
            clientTransport.UseColoc(new());

        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseColoc(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport,
            SlicOptions options) =>
            clientTransport.Add(TransportNames.Coloc, new SlicClientTransport(new ColocClientTransport(), options));

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseTcp(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport) =>
            clientTransport.UseTcp(new TcpOptions(), new SlicOptions());

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseTcp(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions) =>
            clientTransport.Add(TransportNames.Tcp,
                                new SlicClientTransport(new TcpClientTransport(tcpOptions, null), slicOptions));

        /// <summary>Adds the tcp client transport with ssl support to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseTcp(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.UseTcp(new TcpOptions(), new SlicOptions(), authenticationOptions);

        /// <summary>Adds the tcp client transport with ssl support to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseTcp(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.Add(
                TransportNames.Tcp,
                new SlicClientTransport(new TcpClientTransport(tcpOptions, authenticationOptions), slicOptions));
    }
}
