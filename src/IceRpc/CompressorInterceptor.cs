// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An interceptor that compresses the 2.0 encoded payload of a request, when
    /// <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
    public class CompressorInterceptor : IInvoker
    {
        /// <summary>Options class to configure the <see cref="CompressorInterceptor"/>.</summary>
        public sealed class Options
        {
            /// <summary>The compression level for the compress operation, the default value is
            /// <see cref="CompressionLevel.Fastest"/>.</summary>
            public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;
            /// <summary>The minimum size in bytes of the request payload to which apply compression. The default value
            /// is 500.</summary>
            public int CompressionMinSize { get; set; } = 500;
            /// <summary>Whether or not to apply compression to the 2.0 encoded payload of a request when
            /// <see cref="Features.CompressPayload.Yes"/> is present in the request features. The default value is
            /// <c>true</c>.</summary>
            public bool CompressRequestPayload { get; set; } = true;
            /// <summary>Whether or not to decompress the compressed response payload. The default value is
            /// <c>true</c>.</summary>
            public bool DecompressResponsePayload { get; set; } = true;
        }

        private readonly IInvoker _next;
        private readonly Options _options;

        /// <summary>Constructs a compressor interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="options">The options to configure the compressor.</param>
        public CompressorInterceptor(IInvoker next, Options options)
        {
            _next = next;
            _options = options;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // TODO: rename CompressRequestPayload to CompressRequest or add CompressStreamParam?
            if (_options.CompressRequestPayload &&
                request.PayloadEncoding == Encoding.Ice20 &&
                request.Features[typeof(Features.CompressPayload)] == Features.CompressPayload.Yes)
            {
                if (request.PayloadSize >= 1 &&
                    request.Payload.Span[0].Span[0] == (byte)CompressionFormat.NotCompressed)
                {
                    (CompressionResult result, ReadOnlyMemory<byte> compressedPayload) =
                        request.Payload.Compress(request.PayloadSize,
                                                 _options.CompressionLevel,
                                                 _options.CompressionMinSize);
                    if (result == CompressionResult.Success)
                    {
                        request.Payload = new ReadOnlyMemory<byte>[] { compressedPayload };
                    }
                }

                request.StreamCompressor =
                    outputStream => outputStream.CompressStream(_options.CompressionLevel);
            }

            // TODO: rename DecompressResponsePayload to DecompressResponse or add DecompressStreamParam?
            if (_options.DecompressResponsePayload)
            {
                // TODO: check for response Features.DecompressPayload?
                request.StreamDecompressor =
                    (compressFormat, inputStream) => inputStream.DecompressStream(compressFormat);
            }

            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            if (_options.DecompressResponsePayload &&
                response.ResultType == ResultType.Success &&
                response.PayloadEncoding == Encoding.Ice20 &&
                response.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
            {
                ReadOnlyMemory<byte> payload = await response.GetPayloadAsync(cancel).ConfigureAwait(false);

                if (payload.Length >= 1 && payload.Span[0] == (byte)CompressionFormat.Deflate)
                {
                    response.Payload = payload.Decompress(
                        maxSize: request.Connection!.IncomingFrameMaxSize);
                }
            }

            return response;
        }
    }
}
