// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging messages in "IceRpc" category.</summary>
    internal static class LoggerExtensions
    {
        internal const int ProtocolBaseEventId = 1 * EventIdRange;
        internal const int ServerBaseEventId = 2 * EventIdRange;
        internal const int SlicBaseEventId = 3 * EventIdRange;
        internal const int TlsBaseEventId = 4 * EventIdRange;
        internal const int TransportBaseEventId = 5 * EventIdRange;
        internal const int ConnectionBaseEventId = 6 * EventIdRange;
        internal const int LocationBaseEventId = 7 * EventIdRange;
        private const int EventIdRange = 128;
    }
}
