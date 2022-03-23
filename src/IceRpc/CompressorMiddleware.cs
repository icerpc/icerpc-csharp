// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>A middleware that compresses the 2.0 encoded payload of a response when
    /// <see cref="Features.CompressPayload.Yes"/> is present in the response features.</summary>
    public class CompressorMiddleware : IDispatcher
    {
        private static readonly ReadOnlySequence<byte> _encodedCompressionFormatValue =
            new(new byte[] { (byte)CompressionFormat.Deflate });

        private readonly IDispatcher _next;
        private readonly CompressionLevel _compressionLevel;

        /// <summary>Constructs a compressor middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        public CompressorMiddleware(IDispatcher next, CompressionLevel compressionLevel = CompressionLevel.Fastest)
        {
            _next = next;
            _compressionLevel = compressionLevel;
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (request.Protocol.HasFields && request.Fields.ContainsKey(RequestFieldKey.CompressionFormat))
            {
                CompressionFormat compressionFormat = request.Fields.DecodeValue(
                    RequestFieldKey.CompressionFormat,
                    (ref SliceDecoder decoder) => decoder.DecodeCompressionFormat());

                if (compressionFormat == CompressionFormat.Deflate)
                {
                    request.Payload = PipeReader.Create(
                        new DeflateStream(request.Payload.AsStream(), CompressionMode.Decompress));
                }
                // else don't do anything
            }

            OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);

            // The CompressPayload feature is typically set through the Slice compress attribute.

            if (request.Protocol.HasFields &&
                response.ResultType == ResultType.Success &&
                request.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes &&
                !response.Fields.ContainsKey(ResponseFieldKey.CompressionFormat))
            {
                response.PayloadSink = PipeWriter.Create(
                    new DeflateStream(response.PayloadSink.ToPayloadSinkStream(), _compressionLevel));

                response.Fields = response.Fields.With(
                    ResponseFieldKey.CompressionFormat,
                    _encodedCompressionFormatValue);
            }

            return response;
        }
    }
}
