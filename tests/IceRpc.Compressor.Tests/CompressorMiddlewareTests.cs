// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor.Tests;

public class CompressorMiddlewareTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    /// <summary>Verifies that the compressor middleware installs a payload writer interceptor that compresses the
    /// input using the given compression format when the request carries the compress payload feature.</summary>
    [Test]
    public async Task Compress_response_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With(CompressFeature.Compress);
            var response = new OutgoingResponse(request);
            return new(response);
        });
        var sut = new CompressorMiddleware(dispatcher, compressionFormat);
        var outStream = new MemoryStream();
        var output = PipeWriter.Create(outStream);
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance);

        // Act
        OutgoingResponse response = await sut.DispatchAsync(request);

        // Assert
        PipeWriter payloadWriter = response.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(_payload);

        // Rewind the out stream and check that it was correctly compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using Stream decompressedStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(outStream, CompressionMode.Decompress) :
            new DeflateStream(outStream, CompressionMode.Decompress);
        byte[] decompressedPayload = new byte[4096];
        await decompressedStream.ReadAtLeastAsync(decompressedPayload, _payload.Length);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        payloadWriter.Complete();
    }

    /// <summary>Verifies that the compressor middleware does not install a payload writer interceptor if the request
    /// does not contain the compress payload feature.</summary>
    [Test]
    public async Task Compressor_middleware_without_the_compress_feature_does_not_install_a_payload_writer_interceptor()
    {
        using var dispatcher = new TestDispatcher();
        var sut = new CompressorMiddleware(dispatcher, CompressionFormat.Brotli);
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance);

        OutgoingResponse response = await sut.DispatchAsync(request);

        var pipe = new Pipe();
        Assert.That(response.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        pipe.Reader.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that the compress middleware does not install a payload writer interceptor if the response is
    /// already compressed (the response already has a compression format field).</summary>
    [Test]
    public async Task Compressor_middleware_does_not_install_a_payload_writer_interceptor_if_the_response_is_already_compressed()
    {
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With(CompressFeature.Compress);
            var response = new OutgoingResponse(request);
            response.Fields = response.Fields.With(
                ResponseFieldKey.CompressionFormat,
                new ReadOnlySequence<byte>(new byte[] { (byte)CompressionFormat.Brotli }));
            return new(response);
        });
        var sut = new CompressorMiddleware(dispatcher, CompressionFormat.Brotli);
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance);

        var response = await sut.DispatchAsync(request, default);

        var pipe = new Pipe();
        Assert.That(response.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        pipe.Reader.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that the compressor middleware does not update the request payload when the compression
    /// format is not supported, and lets the request pass through unchanged.</summary>
    [Test]
    public async Task Compressor_middleware_lets_requests_with_unsupported_compression_format_pass_through()
    {
        using var dispatcher = new TestDispatcher();
        var sut = new CompressorMiddleware(dispatcher, CompressionFormat.Brotli);
        using IncomingRequest request = CreateRequestWitCompressionFormat((CompressionFormat)255);
        PipeReader requestPayload = request.Payload;
        await sut.DispatchAsync(request, default);

        Assert.That(request.Payload, Is.EqualTo(requestPayload));
    }

    /// <summary>Verifies that the compressor middleware wraps the request payload with a pipe reader that
    /// decompress it, when the request carries a supported compression format field.</summary>
    [Test]
    public async Task Decompress_request_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        using var dispatcher = new TestDispatcher();
        var sut = new CompressorMiddleware(dispatcher, compressionFormat);
        using IncomingRequest request = CreateRequestWitCompressionFormat(compressionFormat);
        request.Payload = PipeReader.Create(CreateCompressedPayload(_payload, compressionFormat));

        OutgoingResponse response = await sut.DispatchAsync(request, default);

        ReadResult readResult = await request.Payload.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(_payload));
    }

    private static Stream CreateCompressedPayload(byte[] data, CompressionFormat compressionFormat)
    {
        if (compressionFormat != CompressionFormat.Brotli && compressionFormat != CompressionFormat.Deflate)
        {
            throw new ArgumentException(
                $"Compression format '{compressionFormat}' not supported",
                nameof(compressionFormat));
        }
        var outStream = new MemoryStream();
        {
            using Stream compressedStream = compressionFormat == CompressionFormat.Brotli ?
                new BrotliStream(outStream, CompressionMode.Compress, true) :
                new DeflateStream(outStream, CompressionMode.Compress, true);
            using var payload = new MemoryStream(data);
            payload.CopyTo(compressedStream);
        }
        outStream.Seek(0, SeekOrigin.Begin);
        return outStream;
    }

    private static IncomingRequest CreateRequestWitCompressionFormat(
        CompressionFormat compressionFormat) =>
        new(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.CompressionFormat] =
                    new ReadOnlySequence<byte>(new byte[] { (byte)compressionFormat })
            }
        };
}
