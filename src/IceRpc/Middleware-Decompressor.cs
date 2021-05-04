// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>A middleware that decompresses the request payload.</summary>
        public static Func<IDispatcher, IDispatcher> Decompressor { get; } =
            next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    if (request.PayloadEncoding == Encoding.V20 &&
                        request.PayloadCompressionFormat != CompressionFormat.Decompressed &&
                        request.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
                    {
                        // TODO move DecompressPayload out of the IncomingFrame class
                        request.DecompressPayload();
                    }
                    return await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                });
    }
}
