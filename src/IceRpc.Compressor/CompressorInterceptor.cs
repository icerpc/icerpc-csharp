// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor;

/// <summary>An interceptor that applies compression to the payload of a request depending on the
/// <see cref="ICompressFeature" /> feature, and decompresses the response payload when it is compressed with a
/// supported format. This interceptor supports the <see cref="CompressionFormat.Deflate"/> and
/// <see cref="CompressionFormat.Brotli"/> compression formats.</summary>
public class CompressorInterceptor : IInvoker
{
    private readonly CompressionFormat _compressionFormat;
    private readonly CompressionLevel _compressionLevel;
    private readonly ReadOnlySequence<byte> _encodedCompressionFormatValue;
    private readonly IInvoker _next;

    /// <summary>Constructs a compressor interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="compressionFormat">The compression format for the compress operation.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    public CompressorInterceptor(
        IInvoker next,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        _next = next;
        if (compressionFormat != CompressionFormat.Brotli && compressionFormat != CompressionFormat.Deflate)
        {
            throw new NotSupportedException($"The compression format '{compressionFormat}' is not supported.");
        }
        _compressionFormat = compressionFormat;
        _compressionLevel = compressionLevel;
        _encodedCompressionFormatValue = new(new byte[] { (byte)compressionFormat });
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
            if (_compressionFormat == CompressionFormat.Brotli)
            {
                request.Use(next => PipeWriter.Create(new BrotliStream(next.AsStream(), _compressionLevel)));
            }
            else
            {
                Debug.Assert(_compressionFormat == CompressionFormat.Deflate);
                request.Use(next => PipeWriter.Create(new DeflateStream(next.AsStream(), _compressionLevel)));
            }

            request.Fields = request.Fields.With(RequestFieldKey.CompressionFormat, _encodedCompressionFormatValue);
        }

        IncomingResponse response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

        if (request.Protocol.HasFields && response.StatusCode == StatusCode.Success)
        {
            CompressionFormat compressionFormat = response.Fields.DecodeValue(
               ResponseFieldKey.CompressionFormat,
               (ref SliceDecoder decoder) => decoder.DecodeCompressionFormat());

            if (compressionFormat == CompressionFormat.Brotli)
            {
                response.Payload = PipeReader.Create(
                    new BrotliStream(response.Payload.AsStream(), CompressionMode.Decompress));
            }
            else if (compressionFormat == CompressionFormat.Deflate)
            {
                response.Payload = PipeReader.Create(
                    new DeflateStream(response.Payload.AsStream(), CompressionMode.Decompress));
            }
            // else nothing to do
        }

        return response;
    }
}
