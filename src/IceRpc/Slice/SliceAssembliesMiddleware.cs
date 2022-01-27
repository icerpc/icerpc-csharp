// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Reflection;

namespace IceRpc.Slice
{
    /// <summary>A middleware that overwrites which assemblies contain Slice types.</summary>
    public class SliceAssembliesMiddleware : IDispatcher
    {
        private readonly IActivator _activator;
        private readonly IDispatcher _next;

        /// <summary>Constructs a new Slice assemblies middleware.</summary>
        /// <param name="next">The next middleware in the dispatch pipeline.</param>
        /// <param name="assemblies">The assemblies that contain Slice types.</param>
        public SliceAssembliesMiddleware(IDispatcher next, params Assembly[] assemblies)
        {
            _activator = SliceDecoder.GetActivator(assemblies);
            _next = next;
        }

        ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            request.Features = request.Features.With(_activator);
            return _next.DispatchAsync(request, cancel);
        }
    }
}
