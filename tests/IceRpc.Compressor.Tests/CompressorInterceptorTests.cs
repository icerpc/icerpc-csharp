// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor.Tests;

public class CompressorInterceptorTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    /// <summary>Verifies that the compressor interceptor installs a payload writer interceptor that compresses the
    /// input using the given compression format when the request carries the compress payload feature.</summary>
    [Test]
    public async Task Compress_request_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        var outStream = new MemoryStream();
        var output = PipeWriter.Create(outStream);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        PipeWriter payloadWriter = request.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(_payload);

        // Rewind the out stream and check that it was correctly compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using Stream decompressedStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(outStream, CompressionMode.Decompress) :
            new DeflateStream(outStream, CompressionMode.Decompress);
        var decompressedPayload = new byte[4096];
        await decompressedStream.ReadAtLeastAsync(decompressedPayload, _payload.Length);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        payloadWriter.Complete();
    }

    /// <summary>Verifies that the compressor interceptor does not install a payload writer interceptor if the request
    /// does not contain the compress payload feature.</summary>
    [Test]
    public async Task Compressor_interceptor_without_the_compress_feature_does_not_install_a_payload_writer_interceptor()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        Assert.That(request.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        pipe.Reader.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that the compressor interceptor does not install a payload writer interceptor if the request
    /// is already compressed (the request already has a compression format field).</summary>
    [Test]
    public async Task Compressor_interceptor_does_not_install_a_payload_writer_interceptor_if_the_request_is_already_compressed()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        request.Fields = request.Fields.With(
            RequestFieldKey.CompressionFormat,
            new ReadOnlySequence<byte>(new byte[] { (byte)CompressionFormat.Brotli }));

        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        Assert.That(request.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        pipe.Reader.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that the compressor interceptor does not update the response payload when the compression
    /// format is not supported, and lets the response pass through unchanged.</summary>
    [Test]
    public async Task Compressor_interceptor_lets_responses_with_unsupported_compression_format_pass_through()
    {
        PipeReader? initialPayload = null;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            IncomingResponse response = CreateResponseWithCompressionFormat(request, (CompressionFormat)255);
            initialPayload = response.Payload;
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        Assert.That(response.Payload, Is.EqualTo(initialPayload));
    }

    /// <summary>Verifies that the compressor interceptor wraps the response payload with a pipe reader that
    /// decompress it, when the response carries a supported compression format field.</summary>
    [Test]
    public async Task Decompress_response_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            IncomingResponse response = CreateResponseWithCompressionFormat(request, compressionFormat);
            response.Payload = PipeReader.Create(CreateCompressedPayload(_payload, compressionFormat));
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        ReadResult readResult = await response.Payload.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(_payload));
    }

    private static IncomingResponse CreateResponseWithCompressionFormat(
        OutgoingRequest request,
        CompressionFormat compressionFormat) =>
        new(
            request,
            FakeConnectionContext.Instance,
            StatusCode.Ok,
            errorMessage: null,
            new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
            {
                [ResponseFieldKey.CompressionFormat] =
                    new ReadOnlySequence<byte>(new byte[] { (byte)compressionFormat })
            }.ToImmutableDictionary());

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
}
