// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Options class to configure <see cref="CustomCompressor"/> interceptor.</summary>
        public sealed class CompressorOptions
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
                    // TODO: rename CompressRequestPayload to CompressRequest or add CompressStreamParam?
                    if (compressorOptions.CompressRequestPayload &&
                        request.PayloadEncoding == Encoding.Ice20 &&
                        request.Features[typeof(Features.CompressPayload)] == Features.CompressPayload.Yes)
                    {
                        if (request.PayloadSize >= 1 &&
                            request.Payload.Span[0].Span[0] == (byte)CompressionFormat.NotCompressed)
                        {
                            (CompressionResult result, ReadOnlyMemory<byte> compressedPayload) =
                                request.Payload.Compress(request.PayloadSize,
                                                         compressorOptions.CompressionLevel,
                                                         compressorOptions.CompressionMinSize);
                            if (result == CompressionResult.Success)
                            {
                                request.Payload = new ReadOnlyMemory<byte>[] { compressedPayload };
                            }
                        }

                        request.StreamCompressor =
                            outputStream => outputStream.CompressStream(compressorOptions.CompressionLevel);
                    }

                    // TODO: rename DecompressResponsePayload to DecompressResponse or add DecompressStreamParam?
                    if (compressorOptions.DecompressResponsePayload)
                    {
                        // TODO: check for response Features.DecompressPayload?
                        request.StreamDecompressor =
                            (compressFormat, inputStream) => inputStream.DecompressStream(compressFormat);
                    }

                    IncomingResponse response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);

                    if (compressorOptions.DecompressResponsePayload &&
                        response.ResultType == ResultType.Success &&
                        response.PayloadEncoding == Encoding.Ice20 &&
                        response.Features[typeof(Features.DecompressPayload)] != Features.DecompressPayload.No)
                    {
                        ReadOnlyMemory<byte> payload = await response.GetPayloadAsync(cancel).ConfigureAwait(false);

                        if (payload.Length >= 1 && payload.Span[0] == (byte)CompressionFormat.Deflate)
                        {
                            response.Payload = payload.Decompress(
                                maxSize: request.Connection!.Options!.IncomingFrameMaxSize);
                        }
                    }

                    return response;
                });
    }
}
