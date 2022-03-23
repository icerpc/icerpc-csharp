// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Interop.Tests;

public class CompressorInterceptorTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    private static readonly ReadOnlySequence<byte> _deflateEncodedCompressionFormatValue =
        new(new byte[] { (byte)CompressionFormat.Deflate });

    private static readonly ReadOnlySequence<byte> _unknownEncodedCompressionFormatValue =
        new(new byte[] { 255 });

    /// <summary>Verifies that the compressor interceptor wraps the payload sink pipe writer with a pipe writer that
    /// compresses the input using the deflate compression format when the request carries the compress payload
    /// feature.</summary>
    [Test]
    public async Task Compress_request_payload()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var sut = new CompressorInterceptor(invoker);
        var outStream = new MemoryStream();
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            PayloadSink = PipeWriter.Create(outStream)
        };
        request.Features = request.Features.With(Features.CompressPayload.Yes);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert

        await request.PayloadSink.WriteAsync(_payload);
        // Rewind the output stream used to create the payload sink and check that the contents were correctly
        // compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using var deflateStream = new DeflateStream(outStream, CompressionMode.Decompress);
        var decompressedPayload = new byte[4096];
        await deflateStream.ReadAsync(decompressedPayload);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        await request.PayloadSink.CompleteAsync();
    }

    /// <summary>Verifies that the compressor interceptor does not update the payload sink if the request does
    /// not contain the compress feature.</summary>
    [Test]
    public async Task Compressor_interceptor_without_compress_feature_does_not_update_the_payload_sink()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var initialPayloadSing = request.PayloadSink;

        await sut.InvokeAsync(request, default);

        Assert.That(request.PayloadSink, Is.EqualTo(initialPayloadSing));
    }

    /// <summary>Verifies that the compressor interceptor does not update the payload sink if the request is already
    /// compressed (the request already has a compression format field).</summary>
    [Test]
    public async Task Compressor_interceptor_does_not_update_the_payload_sink_if_request_is_already_compressed()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        request.Features = request.Features.With(Features.CompressPayload.Yes);
        request.Fields = request.Fields.With(
            RequestFieldKey.CompressionFormat,
            _deflateEncodedCompressionFormatValue);
        PipeWriter initialPayloadSing = request.PayloadSink;

        await sut.InvokeAsync(request, default);

        Assert.That(request.PayloadSink, Is.EqualTo(initialPayloadSing));
    }

    /// <summary>Verifies that the compressor interceptor does not update the response payload when the compression
    /// format is not supported, and lets the response pass throw unchanged.</summary>
    [Test]
    public async Task Compressor_interceptor_lets_responses_with_unsupported_compression_format_pass_throw()
    {
        PipeReader? initialPayload = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            IncomingResponse response = CreateResponseWitCompressionFormatField(
                request,
                _unknownEncodedCompressionFormatValue);
            initialPayload = response.Payload;
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        Assert.That(response.Payload, Is.EqualTo(initialPayload));
    }

    /// <summary>Verifies that the compressor interceptor wraps the response payload with a pipe reader that
    /// decompress it, when the response carries a deflate compression format field.</summary>
    [Test]
    public async Task Decompress_response_payload()
    {
        var invoker = new InlineInvoker((request, cancel) =>
        {
            IncomingResponse response = CreateResponseWitCompressionFormatField(
                request,
                _deflateEncodedCompressionFormatValue);
            response.Payload = PipeReader.Create(CreateCompressedPayload(_payload));
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        ReadResult readResult = await response.Payload.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(_payload));
    }

    private static IncomingResponse CreateResponseWitCompressionFormatField(
        OutgoingRequest request,
        ReadOnlySequence<byte> compressionFormatField) =>
        new(request)
        {
            Fields = new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
            {
                [ResponseFieldKey.CompressionFormat] = compressionFormatField
            }.ToImmutableDictionary()
        };

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
}
