// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Reflection;

namespace IceRpc
{
    /// <summary>An interceptor that overwrites which assemblies contain Slice types.</summary>
    public class SliceAssembliesInterceptor : IInvoker
    {
        private readonly IIceDecoderFactory<Ice11Decoder> _decoderFactory11;
        private readonly IIceDecoderFactory<Ice20Decoder> _decoderFactory20;
        private readonly IInvoker _next;

        /// <summary>Constructs a new Slice assemblies interceptor.</summary>
        /// <param name="next">The next interceptor in the invocation pipeline.</param>
        /// <param name="assemblies">The assemblies that contain Slice types.</param>
        public SliceAssembliesInterceptor(IInvoker next, params Assembly[] assemblies)
        {
            _decoderFactory11 = new Ice11DecoderFactory(Ice11Decoder.GetActivator(assemblies));
            _decoderFactory20 = new Ice20DecoderFactory(Ice20Decoder.GetActivator(assemblies));
            _next = next;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            if (response.Features.IsReadOnly)
            {
                response.Features = new FeatureCollection(response.Features);
            }
            response.Features.Set(_decoderFactory11);
            response.Features.Set(_decoderFactory20);
            return response;
        }
    }
}
