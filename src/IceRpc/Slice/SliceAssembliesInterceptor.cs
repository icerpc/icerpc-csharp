// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Reflection;

namespace IceRpc.Slice
{
    /// <summary>An interceptor that overwrites which assemblies contain Slice types.</summary>
    public class SliceAssembliesInterceptor : IInvoker
    {
        private readonly IActivator _activator;
        private readonly IInvoker _next;

        /// <summary>Constructs a new Slice assemblies interceptor.</summary>
        /// <param name="next">The next interceptor in the invocation pipeline.</param>
        /// <param name="assemblies">The assemblies that contain Slice types.</param>
        public SliceAssembliesInterceptor(IInvoker next, params Assembly[] assemblies)
        {
            _activator = SliceDecoder.GetActivator(assemblies);
            _next = next;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            response.Features = response.Features.With(_activator);
            return response;
        }
    }
}
