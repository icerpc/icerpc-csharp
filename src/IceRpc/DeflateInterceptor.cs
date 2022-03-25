// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>An interceptor that applies the deflate compression algorithm to the 2.0 encoded payload of a request,
    /// when <see cref="Features.CompressPayload.Yes"/> is present in the request features.</summary>
    public class DeflateInterceptor : IInvoker
    {
        private static readonly ReadOnlySequence<byte> _encodedCompressionFormatValue =
            new(new byte[] { (byte)CompressionFormat.Deflate });

        private readonly IInvoker _next;
        private readonly CompressionLevel _compressionLevel;

        /// <summary>Constructs a compressor interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        public DeflateInterceptor(IInvoker next, CompressionLevel compressionLevel = CompressionLevel.Fastest)
        {
            _next = next;
            _compressionLevel = compressionLevel;
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // The CompressPayload feature is typically set through the Slice compress attribute.

            if (request.Protocol.HasFields &&
                request.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes &&
                !request.Fields.ContainsKey(RequestFieldKey.CompressionFormat))
            {
                request.PayloadSink = PipeWriter.Create(
                    new DeflateStream(request.PayloadSink.ToPayloadSinkStream(), _compressionLevel));

                request.Fields = request.Fields.With(
                    RequestFieldKey.CompressionFormat,
                    _encodedCompressionFormatValue);
            }

            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

            if (request.Protocol.HasFields && response.ResultType == ResultType.Success)
            {
                CompressionFormat compressionFormat = response.Fields.DecodeValue(
                   ResponseFieldKey.CompressionFormat,
                   (ref SliceDecoder decoder) => decoder.DecodeCompressionFormat());

                if (compressionFormat == CompressionFormat.Deflate)
                {
                    response.Payload = PipeReader.Create(
                        new DeflateStream(response.Payload.AsStream(), CompressionMode.Decompress));
                }
            }

            return response;
        }
    }
}
