// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>This class contains base event ID constants used by category specific event IDs enumeration
    /// values.</summary>
    internal static class BaseEventIds
    {
        internal const int Connection = 1 * EventIdRange;
        internal const int Protocol = 2 * EventIdRange;
        internal const int Transport = 3 * EventIdRange;
        internal const int Slic = 4 * EventIdRange;
        internal const int Tcp = 5 * EventIdRange;
        internal const int Udp = 6 * EventIdRange;
        internal const int Location = 7 * EventIdRange;
        internal const int LoggerInterceptor = 8 * EventIdRange;
        internal const int LoggerMiddleware = 9 * EventIdRange;
        internal const int Retry = 10 * EventIdRange;
        private const int EventIdRange = 128;
    }
}
