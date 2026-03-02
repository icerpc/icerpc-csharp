// Copyright (c) ZeroC, Inc.

#pragma once

#include "BuiltinSequences.ice"

["cs:identifier:IceRpc.Slice.Ice"]
module Ice
{
    /// Represents the implicit base interface of all Slice interfaces.
    ["cs:identifier:IceObject"]
    interface \Object
    {
        /// Gets the Slice type IDs of all the interfaces implemented by the target service.
        /// @return The Slice type IDs of all these interfaces, sorted alphabetically.
        idempotent StringSeq ice_ids();

        /// Tests whether the target service implements the specified interface.
        /// @param id The Slice type ID of the interface to test against.
        /// @return True when the target service implements this interface; otherwise, false.
        idempotent bool ice_isA(string id);

        /// Pings the service.
        idempotent void ice_ping();
    }
}
