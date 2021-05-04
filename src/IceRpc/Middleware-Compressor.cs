// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>An middleware that compresses the 2.0 encoded payload of a response, using the default compression
        /// settings, when <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
        public static Func<IDispatcher, IDispatcher> Compressor { get; } =
            CreateCompressor(CompressionLevel.Fastest, 500);

        /// <summary>Creates a middleware that compresses the 2.0 encoded payload of a response when
        /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        /// <param name="compressionMinSize">The minimum size of the response payload to which apply compression.
        /// </param>
        /// <returns>The new compressor middleware.</returns>
        public static Func<IDispatcher, IDispatcher> CreateCompressor(
            CompressionLevel compressionLevel,
            int compressionMinSize) =>
            next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    OutgoingResponse response = await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                    if (response.PayloadEncoding == Encoding.V20 &&
                        response.ResultType == ResultType.Success &&
                        response.PayloadCompressionFormat == CompressionFormat.Decompressed &&
                        response.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes)
                    {
                        // TODO move CompressPayload out of the OutgoingFrame class
                        response.CompressPayload(compressionLevel, compressionMinSize);
                    }
                    return response;
                });
    }
}
