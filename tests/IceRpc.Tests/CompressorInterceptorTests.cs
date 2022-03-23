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

    private static readonly ReadOnlySequence<byte> _gzipEncodedCompressionFormatValue =
        new(new byte[] { 255 });

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
        Assert.That(outStream.Position, Is.InRange(0, 2048));

        outStream.Seek(0, SeekOrigin.Begin);
        using var deflateStream = new DeflateStream(outStream, CompressionMode.Decompress);
        var decompressedPayload = new byte[4096];
        await deflateStream.ReadAsync(decompressedPayload);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        await request.PayloadSink.CompleteAsync();
    }

    [Test]
    public async Task Compress_interceptor_without_compress_feature_does_not_update_the_payload_sink()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var initialPayloadSing = request.PayloadSink;

        await sut.InvokeAsync(request, default);

        Assert.That(request.PayloadSink, Is.EqualTo(initialPayloadSing));
    }

    [Test]
    public async Task Compress_interceptor_does_not_update_the_payload_sink_if_request_is_already_compressed()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        request.Features = request.Features.With(Features.CompressPayload.Yes);
        request.Fields = request.Fields.With(
            RequestFieldKey.CompressionFormat,
            _deflateEncodedCompressionFormatValue);
        var initialPayloadSing = request.PayloadSink;

        await sut.InvokeAsync(request, default);

        Assert.That(request.PayloadSink, Is.EqualTo(initialPayloadSing));
    }

    [Test]
    public async Task Compress_interceptor_lets_responses_with_unsupported_compression_format_pass_throw()
    {
        PipeReader? initialPayload = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            var response = new IncomingResponse(request)
            {
                Fields = new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>> 
                { 
                    [ResponseFieldKey.CompressionFormat] = _gzipEncodedCompressionFormatValue
                }.ToImmutableDictionary()
            };
            initialPayload = response.Payload;
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        var response = await sut.InvokeAsync(request, default);

        Assert.That(response.Payload, Is.EqualTo(initialPayload));
    }

    [Test]
    public async Task Decompress_response_payload()
    {
        PipeReader? initialPayload = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            var response = new IncomingResponse(request)
            {
                Fields = new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                {
                    [ResponseFieldKey.CompressionFormat] = _deflateEncodedCompressionFormatValue
                }.ToImmutableDictionary()
            };
            initialPayload = response.Payload;
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        var response = await sut.InvokeAsync(request, default);

        Assert.That(response.Payload, Is.EqualTo(initialPayload));
    }

    private static IInvoker CreateInvokerReturningCompressedResponse(
        ReadOnlySequence<byte> compressionFormat)
    {
        return new InlineInvoker((request, cancel) =>
        {
            var response = new IncomingResponse(request)
            {
                Fields = new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                {
                    [ResponseFieldKey.CompressionFormat] = compressionFormat
                }.ToImmutableDictionary()
            };
            return Task.FromResult(response);
        });
    }
}
