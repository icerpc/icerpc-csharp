// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;
using System.Net.NetworkInformation;

namespace IceRpc.Transports.Internal
{
    internal static class IPEndPointExtensions
    {
        /// <summary>Check if an IPEndPoint is on the same host, we consider a peer endpoint is on the same host
        /// if its address matches any of the host local addresses.</summary>
        /// <param name="peer">The peer endpoint to check.</param>
        /// <returns><c>True</c> if the peer is on the same host otherwise <c>false</c>.</returns>
        internal static bool IsSameHost(this IPEndPoint peer)
        {
            try
            {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up)
                    {
                        if (networkInterface.GetIPProperties().UnicastAddresses.Any(
                             address => address.Address.Equals(peer.Address)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
