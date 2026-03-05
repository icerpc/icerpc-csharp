// Copyright (c) ZeroC, Inc.

#pragma once

// This Slice file should be considered internal to IceRPC, but with public generated code.
// It allows us to use the code generator to map the operations of the pseudo base interface Object.

["cs:identifier:IceRpc.Ice"]
module Ice
{
    /// A sequence of strings representing Slice type IDs.
    sequence<string> TypeIdSeq;

    /// Represents the implicit base interface of all Slice interfaces.
    ["cs:identifier:IceObject"]
    interface \Object
    {
        /// Gets the Slice type IDs of all the interfaces implemented by the target service.
        /// @return The Slice type IDs of all these interfaces, sorted alphabetically.
        idempotent TypeIdSeq ice_ids();

        /// Tests whether the target service implements the specified interface.
        /// @param id The Slice type ID of the interface to test against.
        /// @return True when the target service implements this interface; otherwise, false.
        idempotent bool ice_isA(string id);

        /// Pings the service.
        idempotent void ice_ping();
    }
}
