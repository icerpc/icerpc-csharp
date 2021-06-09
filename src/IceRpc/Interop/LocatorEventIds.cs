// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>This enum contains event ID constants used for locator interceptor logging.</summary>
    public enum LocatorEvent
    {
        /// <summary>A locator cache entry was cleared.</summary>
        ClearCacheEntry = Internal.LoggerExtensions.LocatorBaseEventId,
        /// <summary>The locator could not resolve an endpoint.</summary>
        CouldNotResolveEndpoint,
        /// <summary>The locator found an entry in its cache.</summary>
        FoundEntryInCache,
        /// <summary>The locator received an invalid proxy.</summary>
        ReceivedInvalidProxy,
        /// <summary>There was a failure resolving an endpoint.</summary>
        ResolveFailure,
        /// <summary>The locator successfully resolved an endpoint.</summary>
        Resolved,
        /// <summary>The locator is resolving an endpoint.</summary>
        Resolving
    }
}
