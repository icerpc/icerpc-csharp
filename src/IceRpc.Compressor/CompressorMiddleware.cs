// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Slice;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor;

/// <summary>Represents a middleware that decompresses the payloads of incoming requests and compresses the payloads of
/// outgoing responses.</summary>
/// <remarks>This middleware decompresses the payload of an incoming request when the request carries a
/// <see cref="RequestFieldKey.CompressionFormat" /> field with a supported compression format (currently
/// <see cref="CompressionFormat.Brotli" /> or <see cref="CompressionFormat.Deflate" />).<br/>
/// This middleware compresses the payload of a response and sets the <see cref="ResponseFieldKey.CompressionFormat" />
/// field when the request has the <see cref="ICompressFeature" /> feature set and the response's CompressionFormat
/// field is unset.</remarks>
public class CompressorMiddleware : IDispatcher
{
    private readonly CompressionFormat _compressionFormat;
    private readonly CompressionLevel _compressionLevel;
    private readonly ReadOnlySequence<byte> _encodedCompressionFormatValue;
    private readonly IDispatcher _next;

    /// <summary>Constructs a Compressor middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="compressionFormat">The compression format for the compress operation.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    public CompressorMiddleware(
        IDispatcher next,
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
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Protocol.HasFields && request.Fields.ContainsKey(RequestFieldKey.CompressionFormat))
        {
            CompressionFormat compressionFormat = request.Fields.DecodeValue(
                RequestFieldKey.CompressionFormat,
                (ref SliceDecoder decoder) => decoder.DecodeCompressionFormat());

            if (compressionFormat == CompressionFormat.Brotli)
            {
                request.Payload = PipeReader.Create(
                    new BrotliStream(request.Payload.AsStream(), CompressionMode.Decompress));

#if !NET8_0_OR_GREATER
                // Work around bug from StreamPipeReader with the BugFixStreamPipeReaderDecorator
                request.Payload = new BugFixStreamPipeReaderDecorator(request.Payload);
#endif
            }
            else if (compressionFormat == CompressionFormat.Deflate)
            {
                request.Payload = PipeReader.Create(
                    new DeflateStream(request.Payload.AsStream(), CompressionMode.Decompress));

#if !NET8_0_OR_GREATER
                // Work around bug from StreamPipeReader with the BugFixStreamPipeReaderDecorator
                request.Payload = new BugFixStreamPipeReaderDecorator(request.Payload);
#endif
            }
            // else nothing to do
        }

        OutgoingResponse response = await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);

        // The ICompressFeature is typically set through the Slice compress attribute.

        if (request.Protocol.HasFields &&
            response.StatusCode == StatusCode.Success &&
            request.Features.Get<ICompressFeature>() is ICompressFeature compress &&
            compress.Value &&
            !response.Fields.ContainsKey(ResponseFieldKey.CompressionFormat))
        {
            if (_compressionFormat == CompressionFormat.Brotli)
            {
                response.Use(next => PipeWriter.Create(new BrotliStream(next.AsStream(), _compressionLevel)));
            }
            else
            {
                Debug.Assert(_compressionFormat == CompressionFormat.Deflate);
                response.Use(next => PipeWriter.Create(new DeflateStream(next.AsStream(), _compressionLevel)));
            }

            response.Fields = response.Fields.With(ResponseFieldKey.CompressionFormat, _encodedCompressionFormatValue);
        }

        return response;
    }
}
