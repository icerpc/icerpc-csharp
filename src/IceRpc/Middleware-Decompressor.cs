// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>A middleware that decompresses the request payload.</summary>
        public static Func<IDispatcher, IDispatcher> Decompressor { get; } =
            next => new InlineDispatcher(
                (request, cancel) =>
                {
                    if (request.PayloadEncoding == Encoding.V20 &&
                        request.PayloadCompressionFormat != CompressionFormat.Decompressed &&
                        request.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
                    {
                        // TODO maxSize should come from the connection
                        request.Payload = request.Payload.Decompress(request.Protocol,
                                                                     request: true,
                                                                     maxSize: 1024 * 1024);
                    }
                    return next.DispatchAsync(request, cancel);
                });
    }
}
