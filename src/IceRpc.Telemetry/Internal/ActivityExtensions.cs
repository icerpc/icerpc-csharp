// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Net;

namespace IceRpc.Telemetry.Internal;

/// <summary>Provides an extension method for <see cref="Activity" /> to record the network address of the peer.
/// </summary>
internal static class ActivityExtensions
{
    extension(Activity activity)
    {
        /// <summary>Sets the <c>network.peer.address</c> and <c>network.peer.port</c> tags when the remote network
        /// address of the connection is an IP address.</summary>
        /// <param name="connectionContext">The context of the connection that received the request or response.
        /// </param>
        internal void SetNetworkPeer(IConnectionContext connectionContext)
        {
            if (connectionContext.TransportConnectionInformation.RemoteNetworkAddress is IPEndPoint remoteAddress)
            {
                activity.SetTag("network.peer.address", remoteAddress.Address.ToString());
                activity.SetTag("network.peer.port", remoteAddress.Port);
            }
        }
    }
}
