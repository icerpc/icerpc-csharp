// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for connection logging.</summary>
    public static class ConnectionEventIds
    {
        public static readonly EventId DispatchException = new (BaseEventId + 0, nameof(DispatchException));
        public static readonly EventId DispatchCanceledByClient =
            new (BaseEventId + 1, nameof(DispatchCanceledByClient));

        private const int BaseEventId = Internal.LoggerExtensions.ConnectionBaseEventId;
    }
}
