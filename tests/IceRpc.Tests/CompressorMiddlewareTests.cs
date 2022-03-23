// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Interop.Tests;

public class CompressorMiddlewareTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    private static readonly ReadOnlySequence<byte> _deflateEncodedCompressionFormatValue =
        new(new byte[] { (byte)CompressionFormat.Deflate });

    private static readonly ReadOnlySequence<byte> _unknownEncodedCompressionFormatValue =
        new(new byte[] { 255 });

    /// <summary>Verifies that the compressor middleware wraps the payload sink pipe writer with a pipe writer that
    /// compresses the input using the deflate compression format when the request carries the compress payload
    /// feature.</summary>
    [Test]
    public async Task Compress_respose_payload()
    {
        // Arrange
        var outStream = new MemoryStream();
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With(Features.CompressPayload.Yes);
            var response = new OutgoingResponse(request);
            response.PayloadSink = PipeWriter.Create(outStream);
            return new(response);
        });
        var sut = new CompressorMiddleware(dispatcher);

        // Act
        OutgoingResponse response = await sut.DispatchAsync(new IncomingRequest(Protocol.IceRpc));

        // Assert
        await response.PayloadSink.WriteAsync(_payload);
        // Rewind the output stream used to create the payload sink and check that the contents were correctly
        // compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using var deflateStream = new DeflateStream(outStream, CompressionMode.Decompress);
        byte[] decompressedPayload = new byte[4096];
        await deflateStream.ReadAsync(decompressedPayload);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        await response.PayloadSink.CompleteAsync();
    }

    /// <summary>Verifies that the compressor middleware does not update the payload sink if the request does
    /// not contain the compress feature.</summary>
    [Test]
    public async Task Compressor_middleware_without_compress_feature_does_not_update_the_payload_sink()
    {
        PipeWriter? initialPayloadSink = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            var response = new OutgoingResponse(request);
            initialPayloadSink = response.PayloadSink;
            return new(response);
        });
        var sut = new CompressorMiddleware(dispatcher);

        OutgoingResponse response = await sut.DispatchAsync(new IncomingRequest(Protocol.IceRpc));

        Assert.That(response.PayloadSink, Is.EqualTo(initialPayloadSink));
    }

    /// <summary>Verifies that the compressor middleware does not update the payload sink if the response is already
    /// compressed (the response already has a compression format field).</summary>
    [Test]
    public async Task Compressor_middleware_does_not_update_the_payload_sink_if_response_is_already_compressed()
    {
        PipeWriter? initialPayloadSink = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With(Features.CompressPayload.Yes);
            var response = new OutgoingResponse(request);
            response.Fields = response.Fields.With(
                ResponseFieldKey.CompressionFormat,
                _deflateEncodedCompressionFormatValue);
            initialPayloadSink = response.PayloadSink;
            return new(response);
        });
        var sut = new CompressorMiddleware(dispatcher);

        var response = await sut.DispatchAsync(new IncomingRequest(Protocol.IceRpc), default);

        Assert.That(response.PayloadSink, Is.EqualTo(initialPayloadSink));
    }

    /// <summary>Verifies that the compressor middleware does not update the request payload when the compression
    /// format is not supported, and lets the request pass throw unchanged.</summary>
    [Test]
    public async Task Compressor_middleware_lets_requests_with_unsupported_compression_format_pass_throw()
    {
        PipeReader? requestPayload = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            requestPayload = request.Payload;
            return new(new OutgoingResponse(request));
        });
        var sut = new CompressorMiddleware(dispatcher);
        IncomingRequest request = CreateRequestWitCompressionFormatField(_unknownEncodedCompressionFormatValue);

        await sut.DispatchAsync(request, default);

        Assert.That(request.Payload, Is.EqualTo(requestPayload));
    }

    /// <summary>Verifies that the compressor middleware wraps the request payload with a pipe reader that
    /// decompress it, when the request carries a deflate compression format field.</summary>
    [Test]
    public async Task Decompress_request_payload()
    {
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new CompressorMiddleware(dispatcher);
        IncomingRequest request = CreateRequestWitCompressionFormatField(_deflateEncodedCompressionFormatValue);
        request.Payload = PipeReader.Create(CreateCompressedPayload(_payload));

        OutgoingResponse response = await sut.DispatchAsync(request, default);

        ReadResult readResult = await request.Payload.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(_payload));
    }

    private static Stream CreateCompressedPayload(byte[] data)
    {
        var outStream = new MemoryStream();
        {
            using var deflateStream = new DeflateStream(outStream, CompressionMode.Compress, true);
            using var payload = new MemoryStream(data);
            payload.CopyTo(deflateStream);
        }
        outStream.Seek(0, SeekOrigin.Begin);
        return outStream;
    }

    private static IncomingRequest CreateRequestWitCompressionFormatField(
        ReadOnlySequence<byte> compressionFormatField) =>
        new(Protocol.IceRpc)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.CompressionFormat] = compressionFormatField
            }
        };
}
