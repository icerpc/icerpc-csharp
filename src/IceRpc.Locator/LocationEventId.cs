// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Locator;

/// <summary>This enum contains event ID constants used by log decorators of ILocationResolver, IEndpointFinder and
/// IEndpointCache.</summary>
public enum LocationEventId
{
    /// <summary>The location resolver is resolving a location.</summary>
    Resolving,

    /// <summary>The location resolver resolved successfully a location.</summary>
    Resolved,

    /// <summary>The location resolver failed to resolve a location.</summary>
    FailedToResolve,

    /// <summary>The server address cache found the requested location.</summary>
    FoundEntry,

    /// <summary>An entry was set in the server address cache.</summary>
    SetEntry,

    /// <summary>An entry was removed from the server address cache.</summary>
    RemovedEntry,

    /// <summary>The server address finder failed to find server address(es) for the given location.</summary>
    FindFailed,

    /// <summary>The server address finder found server address(es) for the given location.</summary>
    Found
}
