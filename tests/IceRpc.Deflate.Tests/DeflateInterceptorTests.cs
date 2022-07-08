// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Deflate.Tests;

public class DeflateInterceptorTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    private static readonly ReadOnlySequence<byte> _deflateEncodedCompressionFormatValue =
        new(new byte[] { (byte)CompressionFormat.Deflate });

    private static readonly ReadOnlySequence<byte> _unknownEncodedCompressionFormatValue =
        new(new byte[] { 255 });

    /// <summary>Verifies that the deflate interceptor installs a payload writer interceptor that compresses the input
    /// using the deflate compression format when the request carries the compress payload feature.</summary>
    [Test]
    public async Task Compress_request_payload()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancel) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc)));
        var sut = new DeflateInterceptor(invoker);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With<ICompressFeature>(CompressFeature.Compress);
        var outStream = new MemoryStream();
        var output = PipeWriter.Create(outStream);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        PipeWriter payloadWriter = request.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(_payload);

        // Rewind the out stream and check that it was correctly compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using var deflateStream = new DeflateStream(outStream, CompressionMode.Decompress);
        var decompressedPayload = new byte[4096];
        await deflateStream.ReadAsync(decompressedPayload);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        await payloadWriter.CompleteAsync();
    }

    /// <summary>Verifies that the deflate interceptor does not install a payload writer interceptor if the request does
    /// not contain the compress payload feature.</summary>
    [Test]
    public async Task Compressor_interceptor_without_the_compress_feature_does_not_install_a_payload_writer_interceptor()
    {
        var invoker = new InlineInvoker((request, cancel) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc)));
        var sut = new DeflateInterceptor(invoker);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        Assert.That(request.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    /// <summary>Verifies that the deflate interceptor does not install a payload writer interceptor if the request is
    /// already compressed (the request already has a compression format field).</summary>
    [Test]
    public async Task Compressor_interceptor_does_not_install_a_payload_writer_interceptor_if_the_request_is_already_compressed()
    {
        var invoker = new InlineInvoker((request, cancel) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc)));
        var sut = new DeflateInterceptor(invoker);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With<ICompressFeature>(CompressFeature.Compress);
        request.Fields = request.Fields.With(
            RequestFieldKey.CompressionFormat,
            _deflateEncodedCompressionFormatValue);

        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        Assert.That(request.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    /// <summary>Verifies that the deflate interceptor does not update the response payload when the compression
    /// format is not supported, and lets the response pass through unchanged.</summary>
    [Test]
    public async Task Compressor_interceptor_lets_responses_with_unsupported_compression_format_pass_through()
    {
        PipeReader? initialPayload = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            IncomingResponse response = CreateResponseWithCompressionFormatField(
                request,
                _unknownEncodedCompressionFormatValue);
            initialPayload = response.Payload;
            return Task.FromResult(response);
        });
        var sut = new DeflateInterceptor(invoker);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        Assert.That(response.Payload, Is.EqualTo(initialPayload));
    }

    /// <summary>Verifies that the deflate interceptor wraps the response payload with a pipe reader that
    /// decompress it, when the response carries a deflate compression format field.</summary>
    [Test]
    public async Task Decompress_response_payload()
    {
        var invoker = new InlineInvoker((request, cancel) =>
        {
            IncomingResponse response = CreateResponseWithCompressionFormatField(
                request,
                _deflateEncodedCompressionFormatValue);
            response.Payload = PipeReader.Create(CreateCompressedPayload(_payload));
            return Task.FromResult(response);
        });
        var sut = new DeflateInterceptor(invoker);
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        ReadResult readResult = await response.Payload.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(_payload));
    }

    private static IncomingResponse CreateResponseWithCompressionFormatField(
        OutgoingRequest request,
        ReadOnlySequence<byte> compressionFormatField) =>
        new(request, FakeConnectionContext.IceRpc, new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
        {
            [ResponseFieldKey.CompressionFormat] = compressionFormatField
        }.ToImmutableDictionary());

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
