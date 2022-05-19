// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Deflate;

/// <summary>A middleware that applies the deflate compression algorithm to the payload of a response depending on
/// the <see cref="ICompressFeature"/> feature.</summary>
public class DeflateMiddleware : IDispatcher
{
    private static readonly ReadOnlySequence<byte> _encodedCompressionFormatValue =
        new(new byte[] { (byte)CompressionFormat.Deflate });

    private readonly IDispatcher _next;
    private readonly CompressionLevel _compressionLevel;

    /// <summary>Constructs a compressor middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    public DeflateMiddleware(
        IDispatcher next,
        CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        _next = next;
        _compressionLevel = compressionLevel;
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancel = default)
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
            request.Features.Get<ICompressFeature>() is ICompressFeature compress &&
            compress.Value &&
            !response.Fields.ContainsKey(ResponseFieldKey.CompressionFormat))
        {
            response.Use(next => PipeWriter.Create(new DeflateStream(next.AsStream(), _compressionLevel)));

            response.Fields = response.Fields.With(
                ResponseFieldKey.CompressionFormat,
                _encodedCompressionFormatValue);
        }

        return response;
    }
}
