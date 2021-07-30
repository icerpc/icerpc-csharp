// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>This enum contains event ID constants used for locator interceptor logging.</summary>
    public enum LocatorEvent
    {
        /// <summary>The locator interceptor is resolving an adapter ID or identity.</summary>
        Resolving = Internal.LoggerExtensions.LocatorBaseEventId,

        /// <summary>The locator interceptor resolved successfully an adapter ID or identity.</summary>
        Resolved,

        /// <summary>The locator interceptor failed to resolve an adapter ID or identity.</summary>
        FailedToResolve,

        /// <summary>The locator interceptor found the requested adapter ID or identity in its cache. This entry
        /// may be stale.</summary>
        FoundEntryInCache,

        /// <summary>The locator interceptor set an entry in its cache. This entry may silently replace an existing
        /// entry.</summary>
        SetEntryInCache,

        /// <summary>The locator interceptor removed an entry from its cache.</summary>
        RemovedEntryFromCache,

        /// <summary>A find operation on the locator proxy failed (returned null or an invalid proxy, or threw an
        /// exception).</summary>
        FindFailed,

        /// <summary>A find operation on the locator proxy succeeded.</summary>
        Found
    }
}
