// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Linq;
using System.Net;

namespace IceRpc
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
                return IPEndpoint.GetLocalAddresses(null, false, false).Any(address => address.Equals(peer.Address));
            }
            catch
            {
            }
            return false;
        }
    }
}
