// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains constants used for connection logging event Ids.</summary>
    public static class ConnectionEventIds
    {
        private const int BaseEventId = Internal.LoggerExtensions.ConnectionBaseEventId;
        public static readonly EventId DispatchException = new (BaseEventId + 0, nameof(DispatchException));
        public static readonly EventId DispatchCanceledByClient =
            new (BaseEventId + 1, nameof(DispatchCanceledByClient));
    }
}
