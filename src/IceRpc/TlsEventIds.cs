// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for Tls logging.</summary>
    public static class TlsEventIds
    {
        public static readonly EventId TlsAuthenticationSucceeded = GetEventId(TlsEvent.TlsAuthenticationSucceeded);

        public static readonly EventId TlsAuthenticationFailed = GetEventId(TlsEvent.TlsAuthenticationFailed);

        private const int BaseEventId = Internal.LoggerExtensions.TlsBaseEventId;

        private enum TlsEvent
        {
            TlsAuthenticationSucceeded = BaseEventId,
            TlsAuthenticationFailed
        }

        private static EventId GetEventId(TlsEvent e) => new((int)e, e.ToString());
    }
}
