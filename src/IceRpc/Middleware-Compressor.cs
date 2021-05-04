// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>A middleware compressor that uses the default settings.</summary>
        public static Func<IDispatcher, IDispatcher> Compressor { get; } =
            CreateCompressor(CompressionLevel.Fastest, 500);

        /// <summary>Creates a middleware that compresses the response payload when
        /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        /// <param name="compressionMinSize">The minimum size of the request payload to which apply compression.
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
                        (response.Features[typeof(CompressPayload)] == CompressPayload.Yes ||
                         request.Connection.CompressPayload) &&
                        response.PayloadCompressionFormat == CompressionFormat.Decompressed)
                    {
                        response.CompressPayload(compressionLevel, compressionMinSize);
                    }
                    return response;
                });
    }
}
