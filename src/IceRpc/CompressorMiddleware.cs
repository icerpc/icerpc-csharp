// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A middleware that compresses the 2.0 encoded payload of a response when
    /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
    public class CompressorMiddleware : IDispatcher
    {
        private readonly IDispatcher _next;
        private readonly CompressOptions _options;

        /// <summary>Constructs a compressor middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="options">The options to configure the compressor middleware.</param>
        public CompressorMiddleware(IDispatcher next, CompressOptions options)
        {
            _next = next;
            _options = options;
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (_options.DecompressPayload &&
                request.PayloadEncoding == Encoding.Ice20 &&
                request.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
            {
                ReadOnlyMemory<byte> payload = await request.GetPayloadAsync(cancel).ConfigureAwait(false);

                if (payload.Length >= 1 && payload.Span[0] == (byte)CompressionFormat.Deflate)
                {
                    request.Payload = payload.Decompress(maxSize: request.Connection.IncomingFrameMaxSize);
                }
            }

            // TODO: rename DecompressRequestPayload to DecompressRequest or add DecompressStreamParam?
            if (_options.DecompressPayload)
            {
                // TODO: check for request Features.DecompressPayload?
                request.StreamDecompressor =
                    (compressFormat, inputStream) => inputStream.DecompressStream(compressFormat);
            }

            OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);

            // TODO: rename CompressResponsePayload to CompressResponse or add CompressStreamParam?
            if (_options.CompressPayload &&
                response.PayloadEncoding == Encoding.Ice20 &&
                response.ResultType == ResultType.Success &&
                response.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes)
            {
                if (response.PayloadSize >= 1 &&
                    response.Payload.Span[0].Span[0] == (byte)CompressionFormat.NotCompressed)
                {
                    (CompressionResult result, ReadOnlyMemory<byte> compressedPayload) =
                        response.Payload.Compress(response.PayloadSize,
                                                  _options.CompressionLevel,
                                                  _options.CompressionMinSize);
                    if (result == CompressionResult.Success)
                    {
                        response.Payload = new ReadOnlyMemory<byte>[] { compressedPayload };
                    }
                }

                response.StreamCompressor =
                    outputStream => outputStream.CompressStream(_options.CompressionLevel);
            }

            return response;
        }
    }
}
