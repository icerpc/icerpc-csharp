// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for connection logging.</summary>
    public static class ConnectionEventIds
    {
        public static readonly EventId DispatchException = GetEventId(ConnectionEvent.DispatchException);
        public static readonly EventId DispatchCanceledByClient = GetEventId(ConnectionEvent.DispatchCanceledByClient);

        private const int BaseEventId = Internal.LoggerExtensions.ConnectionBaseEventId;

        private enum ConnectionEvent
        {
            DispatchException = BaseEventId,
            DispatchCanceledByClient
        }

        private static EventId GetEventId(ConnectionEvent e) => new((int)e, e.ToString());
    }
}
