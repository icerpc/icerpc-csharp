﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc
{
    /// <summary>An interceptor that compresses the 2.0 encoded payload of a request, when
    /// <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
    public class CompressorInterceptor : IInvoker
    {
        private readonly IInvoker _next;
        private readonly Configure.CompressOptions _options;

        /// <summary>Constructs a compressor interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="options">The options to configure the compressor.</param>
        public CompressorInterceptor(IInvoker next, Configure.CompressOptions options)
        {
            _next = next;
            _options = options;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // TODO: rename CompressPayload to CompressRequest or add CompressStreamParam?
            if (_options.CompressPayload &&
                request.Features[typeof(Features.CompressPayload)] == Features.CompressPayload.Yes)
            {
                request.CompressPayload(_options);

                request.StreamCompressor = outputStream => outputStream.CompressStream(_options.CompressionLevel);
            }

            // TODO: rename DecompressPayload to DecompressResponse or add DecompressStreamParam?
            if (_options.DecompressPayload)
            {
                // TODO: check for response Features.DecompressPayload?
                request.StreamDecompressor =
                    (compressFormat, inputStream) => inputStream.DecompressStream(compressFormat);
            }

            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            if (_options.DecompressPayload &&
                response.ResultType == ResultType.Success &&
                response.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
            {
                response.DecompressPayload();
            }

            return response;
        }
    }
}
