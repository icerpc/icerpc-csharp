// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Configure
{
    /// <summary>Extension methods for class <see cref="ClientTransport"/>.</summary>
    public static class ClientTransportExtensions
    {
        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The client transport being configured.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport UseInteropUdp(this ClientTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpClientTransport());
            return compositeTransport;
        }

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The client transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport UseInteropUdp(this ClientTransport compositeTransport, UdpOptions options)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpClientTransport(options));
            return compositeTransport;
        }
    }
}
