//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <Ice/Metrics.ice>

[cs:namespace(ZeroC)]
module IceMX
{
    /// Provides information on IceStorm topics.
    class TopicMetrics : Metrics
    {
        /// Number of events published on the topic by publishers.
        long published = 0;

        /// Number of events forwarded on the topic by IceStorm topic links.
        long forwarded = 0;
    }

    /// Provides information on IceStorm subscribers.
    class SubscriberMetrics : Metrics
    {
        /// Number of queued events.
        int queued = 0;

        /// Number of outstanding events.
        int outstanding = 0;

        /// Number of forwarded events.
        long delivered = 0;
    }
}
