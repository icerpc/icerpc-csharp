// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc
{
    /// All services implement interface Service, and all Prx class/struct give access to Service's operations.
    interface Service
    {
        StringSeq ice_ids();
        bool ice_isA();
        void ice_ping();
    }
}
