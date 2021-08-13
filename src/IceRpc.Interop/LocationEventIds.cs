// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This enum contains event ID constants used by log decorators of ILocationResolver, IEndpointFinder and
    /// IEndpointCache.</summary>
    public enum LocationEventIds
    {
        /// <summary>The location resolver is resolving a location.</summary>
        Resolving = Internal.BaseEventIds.Location,

        /// <summary>The location resolver resolved successfully a location.</summary>
        Resolved,

        /// <summary>The location resolver failed to resolve a location.</summary>
        FailedToResolve,

        /// <summary>The endpoint cache found the requested location.</summary>
        FoundEntry,

        /// <summary>An entry was set in the endpoint cache.</summary>
        SetEntry,

        /// <summary>An entry was removed from the endpoint cache.</summary>
        RemovedEntry,

        /// <summary>The endpoint finder failed to find endpoint(s) for the given location.</summary>
        FindFailed,

        /// <summary>The endpoint finder found endpoint(s) for the given location.</summary>
        Found
    }
}
