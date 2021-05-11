// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for locator interceptor logging.</summary>
    public static class LocatorEventIds
    {
        public static readonly EventId ClearCacheEntry = new(BaseEventId + 0, nameof(ClearCacheEntry));
        public static readonly EventId CouldNotResolveEndpoint = new(BaseEventId + 1, nameof(CouldNotResolveEndpoint));
        public static readonly EventId FoundEntryInCache = new(BaseEventId + 2, nameof(FoundEntryInCache));
        public static readonly EventId ReceivedInvalidProxy = new(BaseEventId + 3, nameof(ReceivedInvalidProxy));
        public static readonly EventId ResolveFailure = new(BaseEventId + 4, nameof(ResolveFailure));
        public static readonly EventId Resolved = new(BaseEventId + 5, nameof(Resolved));
        public static readonly EventId Resolving = new(BaseEventId + 6, nameof(Resolving));

        private const int BaseEventId = Internal.LoggerExtensions.LocatorBaseEventId;
    }
}
