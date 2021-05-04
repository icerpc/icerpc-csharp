// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>An interceptor that decompresses the response payload.</summary>
        public static Func<IInvoker, IInvoker> Decompressor { get; } =
            next => new InlineInvoker(
                async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                    if (response.PayloadEncoding == Encoding.V20 &&
                        response.PayloadCompressionFormat != CompressionFormat.Decompressed &&
                        response.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
                    {
                        // TODO move DecompressPayload out of the IncomingFrame class
                        response.DecompressPayload();
                    }
                    return response;
                });
    }
}
