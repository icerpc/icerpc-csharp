// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using IceRpc.Internal;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>An interceptor that compresses the 2.0 encoded payload of a request, using the default compression
        /// settings, when <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
        public static Func<IInvoker, IInvoker> Compressor { get; } =
            CreateCompressor(CompressionLevel.Fastest, 500);

        /// <summary>Creates an interceptor that compresses the 2.0 encoded payload of a request when
        /// <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        /// <param name="compressionMinSize">The minimum size of the request payload to which apply compression.
        /// </param>
        /// <returns>The new compressor interceptor.</returns>
        public static Func<IInvoker, IInvoker> CreateCompressor(
            CompressionLevel compressionLevel,
            int compressionMinSize) =>
            next => new InlineInvoker(
                async (request, cancel) =>
                {
                    if (request.PayloadEncoding == Encoding.V20 &&
                        request.PayloadCompressionFormat == CompressionFormat.Decompressed &&
                        request.Features[typeof(Features.CompressPayload)] == Features.CompressPayload.Yes)
                    {
                        request.CompressPayload(compressionLevel, compressionMinSize);
                    }
                    return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                });
    }
}
