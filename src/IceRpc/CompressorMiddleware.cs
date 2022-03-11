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
        private readonly Configure.CompressOptions _options;

        /// <summary>Constructs a compressor middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="options">The options to configure the compressor middleware.</param>
        public CompressorMiddleware(IDispatcher next, Configure.CompressOptions options)
        {
            _next = next;
            _options = options;
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (request.Protocol.HasFields &&
                _options.DecompressPayload &&
                request.Features.Get<Features.DecompressPayload>() != Features.DecompressPayload.No)
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
                _options.CompressPayload &&
                response.ResultType == ResultType.Success &&
                request.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes &&
                !response.Fields.ContainsKey(ResponseFieldKey.CompressionFormat))
            {
                response.PayloadSink = PipeWriter.Create(
                    new DeflateStream(response.PayloadSink.ToPayloadSinkStream(), _options.CompressionLevel));

                response.Fields = response.Fields.With(
                    ResponseFieldKey.CompressionFormat,
                    _encodedCompressionFormatValue);
            }

            return response;
        }
    }
}
