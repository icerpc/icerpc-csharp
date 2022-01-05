// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// All services implement this interface.
    interface Service
    {
        /// Gets the Ice type IDs of all the interfaces implemented by the target service.
        /// @return The Ice type IDs of all these interfaces, sorted alphabetically.
        ice_ids() -> sequence<string>;

        /// Tests whether the target service implements the specified Slice interface.
        /// @param id The Ice type ID of the Slice interface to test against.
        /// @return True when the target service implements this interface; otherwise, false.
        ice_isA(id: string) -> bool;

        /// Pings the service.
        ice_ping();
    }
}
