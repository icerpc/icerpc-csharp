// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Reflection;

namespace IceRpc.Slice
{
    /// <summary>A middleware that overwrites which assemblies contain Slice types.</summary>
    public class SliceAssembliesMiddleware : IDispatcher
    {
        private readonly IIceDecoderFactory<Ice11Decoder> _decoderFactory11;
        private readonly IIceDecoderFactory<Ice20Decoder> _decoderFactory20;
        private readonly IDispatcher _next;

        /// <summary>Constructs a new Slice assemblies middleware.</summary>
        /// <param name="next">The next middleware in the dispatch pipeline.</param>
        /// <param name="assemblies">The assemblies that contain Slice types.</param>
        public SliceAssembliesMiddleware(IDispatcher next, params Assembly[] assemblies)
        {
            _decoderFactory11 = new Ice11DecoderFactory(Ice11Decoder.GetActivator(assemblies));
            _decoderFactory20 = new Ice20DecoderFactory(Ice20Decoder.GetActivator(assemblies));
            _next = next;
        }

        ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (request.Features.IsReadOnly)
            {
                request.Features = new FeatureCollection(request.Features);
            }
            request.Features.Set(_decoderFactory11);
            request.Features.Set(_decoderFactory20);
            return _next.DispatchAsync(request, cancel);
        }
    }
}
