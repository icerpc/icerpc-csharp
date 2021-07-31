// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>This enum contains event ID constants used for for location-related logging by ILocationResolver,
    /// IEndpointFinder and IEndpointCache..</summary>
    public enum LocationEvent
    {
        /// <summary>The location resolver is resolving a location.</summary>
        Resolving = Internal.LoggerExtensions.LocationBaseEventId,

        /// <summary>The location resolver resolved successfully a location.</summary>
        Resolved,

        /// <summary>The location resolver failed to resolve a location.</summary>
        FailedToResolve,

        /// <summary>The endpoint cache found the requested location.</summary>
        FoundEntry,

        /// <summary>An entry is set in the endpoint cache. This entry may silently replace an existing entry.</summary>
        SetEntry,

        /// <summary>An entry was removed from the endpoint cache.</summary>
        RemovedEntry,

        /// <summary>The endpoint finder failed to find endpoint(s) for the given location.</summary>
        FindFailed,

        /// <summary>The endpoint finder found endpoint(s) for the given location.</summary>
        Found
    }
}
