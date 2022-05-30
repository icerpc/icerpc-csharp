// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains base event ID constants used by category specific event IDs enumeration
    /// values.</summary>
    public static class BaseEventIds
    {
        /// <summary>The base event ID for connection tracing.</summary>
        public const int Connection = 1 * EventIdRange;
        /// <summary>The base event ID for protocol tracing.</summary>
        public const int Protocol = 2 * EventIdRange;
        /// <summary>The base event ID for transport tracing.</summary>
        public const int Transport = 3 * EventIdRange;
        /// <summary>The base event ID for Slic tracing.</summary>
        public const int Slic = 4 * EventIdRange;
        /// <summary>The base event ID for Tcp tracing.</summary>
        public const int Tcp = 5 * EventIdRange;
        /// <summary>The base event ID for Location tracing.</summary>
        public const int Location = 6 * EventIdRange;
        /// <summary>The base event ID for logger interceptor tracing.</summary>
        public const int LoggerInterceptor = 7 * EventIdRange;
        /// <summary>The base event ID for logger middleware tracing.</summary>
        public const int LoggerMiddleware = 8 * EventIdRange;
        /// <summary>The base event ID for retry tracing.</summary>
        public const int Retry = 9 * EventIdRange;
        private const int EventIdRange = 128;
    }
}
