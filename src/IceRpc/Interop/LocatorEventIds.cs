// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>This enum contains event ID constants used for locator interceptor logging.</summary>
    public enum LocatorEvent
    {
        /// <summary>A locator adapter ID cache entry was cleared.</summary>
        ClearAdapterIdCacheEntry = Internal.LoggerExtensions.LocatorBaseEventId,
        /// <summary>A locator well-known cache entry was cleared.</summary>
        ClearWellKnownCacheEntry,
        /// <summary>The locator could not resolve an adapter ID.</summary>
        CouldNotResolveAdapterId,
           /// <summary>The locator could not resolve a well-known proxy</summary>
        CouldNotResolveWellKnown,
        /// <summary>The locator found an adapter ID entry in its cache.</summary>
        FoundAdapterIdEntryInCache,
        /// <summary>The locator found a well-known proxy entry in its cache.</summary>
        FoundWellKnownEntryInCache,
        /// <summary>The locator received an invalid proxy when resolving an adapter ID.</summary>
        ReceivedInvalidProxyForAdapterId,
        /// <summary>The locator received an invalid proxy when resolving a well-known proxy.</summary>
        ReceivedInvalidProxyForWellKnown,
        /// <summary>There was a failure resolving an adapter ID.</summary>
        ResolveAdapterIdFailure,
        /// <summary>There was a failure resolving a well-known proxy.</summary>
        ResolveWellKnownFailure,
        /// <summary>The locator successfully resolved an adapter ID.</summary>
        ResolvedAdapterId,
        /// <summary>The locator successfully resolved a well-known proxy.</summary>
        ResolvedWellKnown,
        /// <summary>The locator is resolving an adapter ID.</summary>
        ResolvingAdapterId,
        /// <summary>The locator is resolving a well-known proxy.</summary>
        ResolvingWellKnown
    }
}
