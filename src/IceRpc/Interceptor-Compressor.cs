// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Interceptor
    {
        public static Func<IInvoker, IInvoker> Compressor { get; } =
            CreateCompressor(CompressionFormat.GZip, CompressionLevel.Fastest, 500);

        /// <summary>Creates an interceptor that compresses the request payload when
        /// <see cref="CompressPayloadFeature.Yes"/> is present in the request features.</summary>
        /// <param name="compressionFormat">The compression format for the compress operation.</param>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        /// <param name="compressionMinSize">The minimum size of the request payload to which apply compression.
        /// </param>
        /// <returns>The compress middleware</returns>
        public static Func<IInvoker, IInvoker> CreateCompressor(
            CompressionFormat compressionFormat,
            CompressionLevel compressionLevel,
            int compressionMinSize) =>
            next => new InlineInvoker(
                async (request, cancel) =>
                {
                    if (request.PayloadEncoding == Encoding.V20 && 
                        request.Features[typeof(CompressPayloadFeature)] == CompressPayloadFeature.Yes &&
                        request.PayloadCompressionFormat == CompressionFormat.Decompressed)
                    {
                        request.CompressPayload(compressionFormat, compressionLevel, compressionMinSize);
                    }
                    return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                });
    }
}
