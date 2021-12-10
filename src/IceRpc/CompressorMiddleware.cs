// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc
{
    /// <summary>A middleware that compresses the 2.0 encoded payload of a response when
    /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
    public class CompressorMiddleware : IDispatcher
    {
        private readonly IDispatcher _next;
        private readonly Configure.CompressOptions _options;

        /// <summary>Constructs a compressor middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="options">The options to configure the compressor middleware.</param>
        public CompressorMiddleware(IDispatcher next, Configure.CompressOptions options)
        {
            _next = next;
            _options = options;
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (_options.DecompressPayload &&
                request.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
            {
                request.UsePayloadDecompressor();
            }

            OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);

            if (_options.CompressPayload &&
                response.ResultType == ResultType.Success &&
                response.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes)
            {
                response.UsePayloadCompressor(_options);
            }

            return response;
        }
    }
}
