// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>This class contains event ID constants used for Tls logging.</summary>
    public static class TlsEventIds
    {
        /// <summary>The TLS authentication operation completed successfully.</summary>
        public static readonly EventId TlsAuthenticationSucceeded = GetEventId(TlsEvent.TlsAuthenticationSucceeded);

        /// <summary>The TSL authentication operation failed.</summary>
        public static readonly EventId TlsAuthenticationFailed = GetEventId(TlsEvent.TlsAuthenticationFailed);

        private const int BaseEventId = IceRpc.Internal.LoggerExtensions.TlsBaseEventId;

        private enum TlsEvent
        {
            TlsAuthenticationSucceeded = BaseEventId,
            TlsAuthenticationFailed
        }

        private static EventId GetEventId(TlsEvent e) => new((int)e, e.ToString());
    }
}
