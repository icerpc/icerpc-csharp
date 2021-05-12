// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Options class to configure CustomCompressor middleware.</summary>
        public class CompressorOptions
        {
            /// <summary>The compression level for the compress operation.</summary>
            public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;
            /// <summary>The minimum size of the response payload to which apply compression.</summary>
            public int CompressionMinSize { get; set; } = 500;
            /// <summary>Whether or not to apply compression the 2.0 encoded payload of a response when
            /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
            public bool CompressResponsePayload { get; set; } = true;
            /// <summary>Whether or not to decompress the compressed request payload.</summary>
            public bool DecompressRequestPayload { get; set; } = true;
        }

        /// <summary>An middleware that compresses the 2.0 encoded payload of a response, using the default compression
        /// settings, when <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
        public static Func<IDispatcher, IDispatcher> Compressor { get; } =
            CustomCompressor(new());

        /// <summary>Creates a middleware that compresses the 2.0 encoded payload of a response when
        /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
        /// <param name="compressorOptions">The options to configure the compressor middleware.</param>
        /// <returns>The new compressor middleware.</returns>
        public static Func<IDispatcher, IDispatcher> CustomCompressor(CompressorOptions compressorOptions) =>
            next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    if (compressorOptions.DecompressRequestPayload &&
                        request.PayloadEncoding == Encoding.V20 &&
                        request.PayloadCompressionFormat != CompressionFormat.Decompressed &&
                        request.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
                    {
                        // TODO maxSize should come from the connection
                        request.Payload = request.Payload.Decompress(request.Protocol,
                                                                     request: true,
                                                                     maxSize: 1024 * 1024);
                    }
                    OutgoingResponse response = await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                    if (compressorOptions.CompressResponsePayload &&
                        response.PayloadEncoding == Encoding.V20 &&
                        response.ResultType == ResultType.Success &&
                        response.PayloadCompressionFormat == CompressionFormat.Decompressed &&
                        response.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes)
                    {
                        // TODO move CompressPayload out of the OutgoingFrame class
                        (CompressionResult result, ArraySegment<byte> compressedPayload) =
                            response.Payload.Compress(request.Protocol,
                                                      request: false,
                                                      compressorOptions.CompressionLevel,
                                                      compressorOptions.CompressionMinSize);
                        if (result == CompressionResult.Success)
                        {
                            response.Payload = new List<ArraySegment<byte>> { compressedPayload };
                        }
                    }
                    return response;
                });
    }
}
