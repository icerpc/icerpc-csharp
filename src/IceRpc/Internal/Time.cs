// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Internal
{
    internal static class Time
    {
        /// <summary>Gets the total elapsed time as a TimeSpan since the system started.</summary>
        internal static TimeSpan Elapsed => TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
    }
}
