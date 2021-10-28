// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This class contains event ID constants used for UDP logging.</summary>
    public enum UdpEventIds
    {
        /// <summary>The server starts receiving datagrams.</summary>
        StartReceivingDatagrams = IceRpc.Internal.BaseEventIds.Udp,

        /// <summary>The client starts sending datagrams.</summary>
        StartSendingDatagrams
    }
}
