// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Configure
{
    /// <summary>Extension methods for class <see cref="ServerTransport"/>.</summary>
    public static class ServerTransportExtensions
    {
        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The server transport being configured.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport UseInteropUdp(this ServerTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpServerTransport());
            return compositeTransport;
        }

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport UseInteropUdp(this ServerTransport compositeTransport, UdpOptions options)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpServerTransport(options));
            return compositeTransport;
        }
    }
}
