//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once


[[suppress-warning(reserved-identifier)]]


#include <Ice/Metrics.ice>


[cs:namespace(ZeroC)]
module IceMX
{
    /// Provides information on Glacier2 sessions.
    class SessionMetrics : Metrics
    {
        /// Number of client requests forwarded.
        int forwardedClient = 0;

        /// Number of server requests forwarded.
        int forwardedServer = 0;

        /// The size of the routing table.
        int routingTableSize = 0;

        /// Number of client requests queued.
        int queuedClient = 0;

        /// Number of server requests queued.
        int queuedServer = 0;

        /// Number of client requests overridden.
        int overriddenClient = 0;

        /// Number of server requests overridden.
        int overriddenServer = 0;
    }
}
