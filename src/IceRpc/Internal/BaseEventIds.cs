// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>This class contains base event ID constants used by category specific event IDs enumeration
    /// values.</summary>
    internal static class BaseEventIds
    {
        /// <summary>The base event ID for connection tracing.</summary>
        internal const int Connection = 1 * EventIdRange;

        /// <summary>The base event ID for protocol tracing.</summary>
        internal const int Protocol = 2 * EventIdRange;

        /// <summary>The base event ID for transport tracing.</summary>
        internal const int Transport = 3 * EventIdRange;

        /// <summary>The base event ID for Slic tracing.</summary>
        internal const int Slic = 4 * EventIdRange;

        /// <summary>The base event ID for Tcp tracing.</summary>
        internal const int Tcp = 5 * EventIdRange;

        private const int EventIdRange = 100;
    }
}
