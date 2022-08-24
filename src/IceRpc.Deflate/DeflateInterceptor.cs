// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Deflate;

/// <summary>An interceptor that applies the deflate compression algorithm to the payload of a request depending on
/// the <see cref="ICompressFeature"/> feature.</summary>
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
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        // The CompressPayload feature is typically set through the Slice compress attribute.

        if (request.Protocol.HasFields &&
            request.Features.Get<ICompressFeature>() is ICompressFeature compress &&
            compress.Value &&
            !request.Fields.ContainsKey(RequestFieldKey.CompressionFormat))
        {
            request.Use(next => PipeWriter.Create(new DeflateStream(next.AsStream(), _compressionLevel)));

            request.Fields = request.Fields.With(
                RequestFieldKey.CompressionFormat,
                _encodedCompressionFormatValue);
        }

        IncomingResponse response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

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
