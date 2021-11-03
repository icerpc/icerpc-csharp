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

    /// <summary>Extension methods for class <see cref="ClientTransport{IMultiplexedNetworkConnection}"/>.</summary>
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
            clientTransport.UseTcp(new TcpClientOptions(), new SlicOptions());

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP client options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport<IMultiplexedNetworkConnection> UseTcp(
            this ClientTransport<IMultiplexedNetworkConnection> clientTransport,
            TcpClientOptions tcpOptions,
            SlicOptions slicOptions) =>
            clientTransport.Add(TransportNames.Tcp,
                                new SlicClientTransport(new TcpClientTransport(tcpOptions), slicOptions));
    }
}
