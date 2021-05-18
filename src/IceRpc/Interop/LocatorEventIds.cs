// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Interop
{
    /// <summary>This class contains event ID constants used for locator interceptor logging.</summary>
    public static class LocatorEventIds
    {
        /// <summary>A locator cache entry was cleared.</summary>
        public static readonly EventId ClearCacheEntry = GetEventId(LocatorEvent.ClearCacheEntry);

        /// <summary>The locator could not resolve an endpoint.</summary>
        public static readonly EventId CouldNotResolveEndpoint = GetEventId(LocatorEvent.CouldNotResolveEndpoint);

        /// <summary>The locator found an entry in its cache.</summary>
        public static readonly EventId FoundEntryInCache = GetEventId(LocatorEvent.FoundEntryInCache);

        /// <summary>The locator received an invalid proxy.</summary>
        public static readonly EventId ReceivedInvalidProxy = GetEventId(LocatorEvent.ReceivedInvalidProxy);

        /// <summary>There was a failure resolving an endpoint.</summary>
        public static readonly EventId ResolveFailure = GetEventId(LocatorEvent.ResolveFailure);

        /// <summary>The locator successfully resolved an endpoint.</summary>
        public static readonly EventId Resolved = GetEventId(LocatorEvent.Resolved);

        /// <summary>The locator is resolving an endpoint.</summary>
        public static readonly EventId Resolving = GetEventId(LocatorEvent.Resolving);

        private const int BaseEventId = Internal.LoggerExtensions.LocatorBaseEventId;

        private enum LocatorEvent
        {
            ClearCacheEntry = BaseEventId,
            CouldNotResolveEndpoint,
            FoundEntryInCache,
            ReceivedInvalidProxy,
            ResolveFailure,
            Resolved,
            Resolving
        }

        private static EventId GetEventId(LocatorEvent e) => new((int)e, e.ToString());
    }
}
