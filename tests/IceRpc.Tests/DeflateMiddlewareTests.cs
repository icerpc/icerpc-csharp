// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public class CompressorMiddlewareTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    private static readonly ReadOnlySequence<byte> _deflateEncodedCompressionFormatValue =
        new(new byte[] { (byte)CompressionFormat.Deflate });

    private static readonly ReadOnlySequence<byte> _unknownEncodedCompressionFormatValue =
        new(new byte[] { 255 });

    /// <summary>Verifies that the deflate middleware installs a payload writer interceptor that compresses the input
    /// using the deflate compression format when the request carries the compress payload feature.</summary>
    [Test]
    public async Task Compress_response_payload()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With(Features.CompressPayload.Yes);
            var response = new OutgoingResponse(request);
            return new(response);
        });
        var sut = new DeflateMiddleware(dispatcher);
        var outStream = new MemoryStream();
        var output = PipeWriter.Create(outStream);

        // Act
        OutgoingResponse response = await sut.DispatchAsync(new IncomingRequest(InvalidConnection.IceRpc));

        // Assert
        PipeWriter payloadWriter = response.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(_payload);

        // Rewind the out stream and check that it was correctly compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using var deflateStream = new DeflateStream(outStream, CompressionMode.Decompress);
        byte[] decompressedPayload = new byte[4096];
        await deflateStream.ReadAsync(decompressedPayload);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        await payloadWriter.CompleteAsync();
    }

    /// <summary>Verifies that the deflate middleware does not install a payload writer interceptor if the request does
    /// not contain the compress payload feature.</summary>
    [Test]
    public async Task Compressor_middleware_without_the_compress_feature_does_not_install_a_payload_writer_interceptor()
    {
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new DeflateMiddleware(dispatcher);

        OutgoingResponse response = await sut.DispatchAsync(new IncomingRequest(InvalidConnection.IceRpc));

        var pipe = new Pipe();
        Assert.That(response.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    /// <summary>Verifies that the deflate middleware does not install a payload writer interceptor if the response is
    /// already compressed (the response already has a compression format field).</summary>
    [Test]
    public async Task Compressor_middleware_does_not_install_a_payload_writer_interceptor_if_the_response_is_already_compressed()
    {
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With(Features.CompressPayload.Yes);
            var response = new OutgoingResponse(request);
            response.Fields = response.Fields.With(
                ResponseFieldKey.CompressionFormat,
                _deflateEncodedCompressionFormatValue);
            return new(response);
        });
        var sut = new DeflateMiddleware(dispatcher);

        var response = await sut.DispatchAsync(new IncomingRequest(InvalidConnection.IceRpc), default);

        var pipe = new Pipe();
        Assert.That(response.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    /// <summary>Verifies that the deflate middleware does not update the request payload when the compression
    /// format is not supported, and lets the request pass through unchanged.</summary>
    [Test]
    public async Task Compressor_middleware_lets_requests_with_unsupported_compression_format_pass_through()
    {
        PipeReader? requestPayload = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            requestPayload = request.Payload;
            return new(new OutgoingResponse(request));
        });
        var sut = new DeflateMiddleware(dispatcher);
        IncomingRequest request = CreateRequestWitCompressionFormatField(_unknownEncodedCompressionFormatValue);

        await sut.DispatchAsync(request, default);

        Assert.That(request.Payload, Is.EqualTo(requestPayload));
    }

    /// <summary>Verifies that the deflate middleware wraps the request payload with a pipe reader that
    /// decompress it, when the request carries a deflate compression format field.</summary>
    [Test]
    public async Task Decompress_request_payload()
    {
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var sut = new DeflateMiddleware(dispatcher);
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
        new(InvalidConnection.IceRpc)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.CompressionFormat] = compressionFormatField
            }
        };
}
