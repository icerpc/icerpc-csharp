// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using IceRpc.Transports;
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
        var pipe = new Pipe();
        var output = new PipeStreamWriter(pipe.Writer);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        IMultiplexedStreamWriter payloadWriter = request.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: true, default);
        await payloadWriter.CompleteAsync(default);

        Assert.That(output.IsEnded, Is.True);
        Assert.That(output.IsCompletedSuccessfully, Is.True);
        Assert.That(await DecompressAsync(pipe.Reader, compressionFormat), Is.EqualTo(_payload));
    }

    [Test]
    public async Task Complete_compressor_payload_writer_writes_the_trailer(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);
        var pipe = new Pipe();
        var output = new PipeStreamWriter(pipe.Writer);
        IMultiplexedStreamWriter payloadWriter = request.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: false, default);

        // Act
        await payloadWriter.CompleteAsync(default);

        // Assert
        Assert.That(output.IsEnded, Is.True);
        Assert.That(await DecompressAsync(pipe.Reader, compressionFormat), Is.EqualTo(_payload));
    }

    [Test]
    public async Task Abort_compressor_payload_writer_aborts_the_output(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        var output = new PipeStreamWriter(pipe.Writer);
        IMultiplexedStreamWriter payloadWriter = request.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: false, default);

        // Act
        payloadWriter.Abort();

        // Assert
        Assert.That(output.IsCompleted, Is.True);
        Assert.That(output.IsCompletedSuccessfully, Is.False);
        Assert.That(
            async () =>
            {
                while (true)
                {
                    ReadResult readResult = await pipe.Reader.ReadAsync();
                    pipe.Reader.AdvanceTo(readResult.Buffer.End);
                    if (readResult.IsCompleted)
                    {
                        break;
                    }
                }
            },
            Throws.InstanceOf<IceRpcException>());
        pipe.Reader.Complete();
    }

    [Test]
    public async Task Complete_compressor_payload_writer_aborts_the_output_when_the_trailer_write_fails(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var failingWriter = new FailingStreamWriter();
        IMultiplexedStreamWriter payloadWriter = request.GetPayloadWriter(failingWriter);
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: false, default);
        failingWriter.FailWrites = true;

        // Act/Assert
        Assert.That(async () => await payloadWriter.CompleteAsync(default), Throws.InstanceOf<IOException>());
        Assert.That(failingWriter.IsCompleted, Is.True);
        Assert.That(failingWriter.IsCompletedSuccessfully, Is.False);
    }

    /// <summary>Verifies that the write of the compression trailer honors the cancellation token: a peer that stops
    /// reading cannot block the completion of the payload writer.</summary>
    [Test]
    public async Task Complete_compressor_payload_writer_is_canceled_when_the_trailer_write_blocks(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var blockingWriter = new BlockingStreamWriter();
        IMultiplexedStreamWriter payloadWriter = request.GetPayloadWriter(blockingWriter);
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: false, default);
        blockingWriter.BlockWrites = true;
        using var cts = new CancellationTokenSource();

        // Act
        ValueTask completeTask = payloadWriter.CompleteAsync(cts.Token);
        await blockingWriter.WriteBlocked;
        cts.Cancel();

        // Assert
        Assert.That(async () => await completeTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(blockingWriter.IsCompleted, Is.True);
        Assert.That(blockingWriter.IsCompletedSuccessfully, Is.False);
    }

    // A stream writer that throws IOException on every write once FailWrites is set.
    private class FailingStreamWriter : IMultiplexedStreamWriter
    {
        public long UnflushedBytes => _pipe.Writer.UnflushedBytes;

        internal bool IsCompleted { get; private set; }

        internal bool IsCompletedSuccessfully { get; private set; }

        internal bool FailWrites { get; set; }

        private readonly Pipe _pipe = new();

        public void Advance(int count) => _pipe.Writer.Advance(count);

        public void Abort()
        {
            if (!IsCompleted)
            {
                IsCompleted = true;
                _pipe.Writer.Complete();
                _pipe.Reader.Complete();
            }
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            if (FailWrites)
            {
                Abort();
                throw new IOException("write failed");
            }
            IsCompleted = true;
            IsCompletedSuccessfully = true;
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            return default;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

        public async ValueTask<WriteResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancellationToken)
        {
            if (FailWrites)
            {
                throw new IOException("write failed");
            }
            foreach (ReadOnlyMemory<byte> segment in source)
            {
                _pipe.Writer.Write(segment.Span);
            }
            await _pipe.Writer.FlushAsync(cancellationToken);
            return WriteResult.Written;
        }
    }

    // A stream writer whose writes block until canceled once BlockWrites is set, like a transport whose peer stopped
    // reading after the send window was consumed.
    private class BlockingStreamWriter : IMultiplexedStreamWriter
    {
        public long UnflushedBytes => _pipe.Writer.UnflushedBytes;

        internal bool BlockWrites { get; set; }

        internal bool IsCompleted { get; private set; }

        internal bool IsCompletedSuccessfully { get; private set; }

        internal Task WriteBlocked => _writeBlockedTcs.Task;

        private readonly Pipe _pipe = new();
        private readonly TaskCompletionSource _writeBlockedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Advance(int count) => _pipe.Writer.Advance(count);

        public void Abort()
        {
            if (!IsCompleted)
            {
                IsCompleted = true;
                _pipe.Writer.Complete();
                _pipe.Reader.Complete();
            }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            await WriteAsync(ReadOnlySequence<byte>.Empty, endStream: true, cancellationToken);
            IsCompleted = true;
            IsCompletedSuccessfully = true;
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

        public async ValueTask<WriteResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancellationToken)
        {
            if (BlockWrites)
            {
                _writeBlockedTcs.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            foreach (ReadOnlyMemory<byte> segment in source)
            {
                _pipe.Writer.Write(segment.Span);
            }
            await _pipe.Writer.FlushAsync(cancellationToken);
            return WriteResult.Written;
        }
    }

    [Test]
    public async Task Compressor_interceptor_without_the_compress_feature_does_not_install_a_payload_writer_interceptor()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        await sut.InvokeAsync(request, default);

        var output = new PipeStreamWriter(new Pipe().Writer);
        Assert.That(request.GetPayloadWriter(output), Is.EqualTo(output));
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

        var output = new PipeStreamWriter(new Pipe().Writer);
        Assert.That(request.GetPayloadWriter(output), Is.EqualTo(output));
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

    private static async Task<byte[]> DecompressAsync(PipeReader reader, CompressionFormat compressionFormat)
    {
        var compressedStream = new MemoryStream();
        await reader.CopyToAsync(compressedStream);
        reader.Complete();
        compressedStream.Seek(0, SeekOrigin.Begin);
        using Stream decompressedStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(compressedStream, CompressionMode.Decompress) :
            new DeflateStream(compressedStream, CompressionMode.Decompress);
        var decompressed = new MemoryStream();
        await decompressedStream.CopyToAsync(decompressed);
        return decompressed.ToArray();
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
}
