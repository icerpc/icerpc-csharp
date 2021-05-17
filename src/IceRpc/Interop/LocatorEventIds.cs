// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Interop
{
    /// <summary>This class contains event ID constants used for locator interceptor logging.</summary>
    public static class LocatorEventIds
    {
        public static readonly EventId ClearCacheEntry = GetEventId(LocatorEvent.ClearCacheEntry);
        public static readonly EventId CouldNotResolveEndpoint = GetEventId(LocatorEvent.CouldNotResolveEndpoint);
        public static readonly EventId FoundEntryInCache = GetEventId(LocatorEvent.FoundEntryInCache);
        public static readonly EventId ReceivedInvalidProxy = GetEventId(LocatorEvent.ReceivedInvalidProxy);
        public static readonly EventId ResolveFailure = GetEventId(LocatorEvent.ResolveFailure);
        public static readonly EventId Resolved = GetEventId(LocatorEvent.Resolved);
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
