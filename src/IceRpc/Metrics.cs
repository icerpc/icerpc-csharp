// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics.Tracing;

namespace IceRpc
{ 
    public static class Metrics
    {
        /// <summary>Creates a new event source that can be used to publish IceRpc dispatch metrics,
        /// <see cref="Middleware.CreateMetricsPublisher(EventSource)"/>.</summary>
        /// <param name="name">The name for the event source</param>
        /// <returns>A new event source.</returns>
        public static EventSource CreateDispatchEventSource(string name) =>
            new DispatchEventSource(name);

        /// <summary>Creates a new event source that can be used to publish IceRpc invocation metrics,
        /// <see cref="Interceptor.CreateMetricsPublisher(EventSource)"/>.</summary>
        /// <param name="name">The name for the event source</param>
        /// <returns>A new event source.</returns>
        public static EventSource CreateInvocationEventSource(string name) =>
            new InvocationEventSource(name);
    }
}
