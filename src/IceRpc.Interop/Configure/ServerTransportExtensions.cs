﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Configure
{
    /// <summary>Extension methods for class <see cref="ServerTransport"/>.</summary>
    public static class ServerTransportExtensions
    {
        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The server transport being configured.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport UseInteropUdp(this ServerTransport serverTransport) =>
            serverTransport.UseInteropUdp(new UdpOptions());

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport UseInteropUdp(this ServerTransport serverTransport, UdpOptions options)
        {
            serverTransport.Add(TransportNames.Udp, Protocol.Ice1, new UdpServerTransport(options));
            return serverTransport;
        }
    }
}
