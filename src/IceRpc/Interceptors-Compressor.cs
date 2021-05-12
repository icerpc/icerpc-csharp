// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Options class to configure CustomCompressor interceptor.</summary>
        public class CompressorOptions
        {
            /// <summary>The compression level for the compress operation.</summary>
            public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Fastest;
            /// <summary>The minimum size of the request payload to which apply compression.</summary>
            public int CompressionMinSize { get; set; } = 500;
            /// <summary>Whether or not to apply compression the 2.0 encoded payload of a request when
            /// <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
            public bool CompressRequestPayload { get; set; } = true;
            /// <summary>Whether or not to decompress the compressed response payload.</summary>
            public bool DecompressResponsePayload { get; set; } = true;
        }

        /// <summary>An interceptor that compresses the 2.0 encoded payload of a request, using the default compression
        /// settings, when <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
        public static Func<IInvoker, IInvoker> Compressor { get; } =
            CustomCompressor(new());

        /// <summary>Creates an interceptor that compresses the 2.0 encoded payload of a request when
        /// <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
        /// <param name="compressorOptions">The compression options to configure the compressor.</param>
        /// <returns>The new compressor interceptor.</returns>
        public static Func<IInvoker, IInvoker> CustomCompressor(CompressorOptions compressorOptions) =>
            next => new InlineInvoker(
                async (request, cancel) =>
                {
                    if (compressorOptions.CompressRequestPayload &&
                        request.PayloadEncoding == Encoding.V20 &&
                        request.PayloadCompressionFormat == CompressionFormat.Decompressed &&
                        request.Features[typeof(Features.CompressPayload)] == Features.CompressPayload.Yes)
                    {
                        (CompressionResult result, ArraySegment<byte> compressedPayload) =
                            request.Payload.Compress(request.Protocol,
                                                     request: true,
                                                     compressorOptions.CompressionLevel,
                                                     compressorOptions.CompressionMinSize);
                        if (result == CompressionResult.Success)
                        {
                            request.Payload = new List<ArraySegment<byte>> { compressedPayload };
                        }
                    }
                    var response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                    if (compressorOptions.DecompressResponsePayload &&
                        response.PayloadEncoding == Encoding.V20 &&
                        response.PayloadCompressionFormat != CompressionFormat.Decompressed &&
                        response.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
                    {
                        // TODO maxSize should come from the connection
                        response.Payload = response.Payload.Decompress(response.Protocol,
                                                                       request: false,
                                                                       maxSize: 1024 * 1024);
                    }
                    return response;
                });
    }
}
