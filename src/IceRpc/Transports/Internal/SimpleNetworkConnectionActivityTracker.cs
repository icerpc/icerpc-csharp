// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>A helper class to keep track of the last activity of a simple network connection.</summary>
    internal class SimpleNetworkConnectionActivityTracker
    {
        internal TimeSpan LastActivity => TimeSpan.FromTicks(_lastActivity * TimeSpan.TicksPerMillisecond);

        private long _lastActivity = Environment.TickCount64;

        internal void Update() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
    }
}
