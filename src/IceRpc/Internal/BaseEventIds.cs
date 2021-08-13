// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>This class contains base event ID constants used by category specific event IDs enumeration
    /// values.</summary>
    internal static class BaseEventIds
    {
        internal const int Protocol = 1 * EventIdRange;
        internal const int Server = 2 * EventIdRange;
        internal const int Slic = 3 * EventIdRange;
        internal const int Tls = 4 * EventIdRange;
        internal const int Transport = 5 * EventIdRange;
        internal const int Location = 6 * EventIdRange;
        internal const int LoggerInterceptor = 7 * EventIdRange;
        internal const int LoggerMiddleware = 8 * EventIdRange;
        internal const int Retry = 9 * EventIdRange;
        private const int EventIdRange = 128;
    }
}
