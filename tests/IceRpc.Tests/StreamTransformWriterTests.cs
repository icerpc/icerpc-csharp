// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace IceRpc.Tests;

public class StreamTransformWriterTests
{
    private static readonly byte[] _trailer = Encoding.ASCII.GetBytes("<trailer>");

    [Test]
    public async Task Bytes_written_flow_through_the_transform()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));
        byte[] head = Encoding.ASCII.GetBytes("hello ");
        byte[] tail = Encoding.ASCII.GetBytes("world");

        // Act
        head.CopyTo(sut.GetSpan(head.Length));
        sut.Advance(head.Length);
        WriteResult result = await sut.WriteAsync(new ReadOnlySequence<byte>(tail), endStream: false, default);

        // Assert
        Assert.That(result, Is.EqualTo(WriteResult.Written));
        ReadResult readResult = await pipe.Reader.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("hello world")));
        Assert.That(decoratee.IsEnded, Is.False);

        // Cleanup
        pipe.Reader.Complete();
        await sut.CompleteAsync(default);
    }

    [Test]
    public async Task Large_write_is_transformed_in_chunks()
    {
        // Arrange: pauseWriterThreshold: 0 so that the writes to the pipe never block.
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var decoratee = new PipeStreamWriter(pipe.Writer);
        TrailerStream? transformStream = null;
        var sut = new StreamTransformWriter(decoratee, stream => transformStream = new TrailerStream(stream));
        byte[] payload = Enumerable.Range(0, 400 * 1024).Select(i => (byte)i).ToArray();

        // Act
        WriteResult result = await sut.WriteAsync(new ReadOnlySequence<byte>(payload), endStream: false, default);

        // Assert
        Assert.That(result, Is.EqualTo(WriteResult.Written));
        Assert.That(transformStream!.WriteCount, Is.EqualTo(4)); // 400 KB in chunks of 128 KB
        ReadResult readResult = await pipe.Reader.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(payload));

        // Cleanup
        pipe.Reader.Complete();
        await sut.CompleteAsync(default);
    }

    [Test]
    public async Task Write_flushes_the_transform()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        var sut = new StreamTransformWriter(decoratee, stream => new BufferingStream(stream));
        byte[] payload = Encoding.ASCII.GetBytes("payload");

        // Act
        WriteResult result = await sut.WriteAsync(new ReadOnlySequence<byte>(payload), endStream: false, default);

        // Assert
        Assert.That(result, Is.EqualTo(WriteResult.Written));
        Assert.That(pipe.Reader.TryRead(out ReadResult readResult), Is.True);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(payload));

        // Cleanup
        pipe.Reader.Complete();
        await sut.CompleteAsync(default);
    }

    [Test]
    public void Canceled_write_through_a_buffering_transform_stops_after_one_chunk()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        BufferingStream? transformStream = null;
        var sut = new StreamTransformWriter(decoratee, stream => transformStream = new BufferingStream(stream));
        byte[] payload = new byte[400 * 1024];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act/Assert
        Assert.That(
            async () => await sut.WriteAsync(new ReadOnlySequence<byte>(payload), endStream: false, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(transformStream!.WriteCount, Is.EqualTo(1));

        // Cleanup
        pipe.Reader.Complete();
        sut.Abort();
    }

    [Test]
    public async Task End_stream_write_sends_the_trailing_bytes()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));
        byte[] payload = Encoding.ASCII.GetBytes("payload");

        // Act
        WriteResult result = await sut.WriteAsync(new ReadOnlySequence<byte>(payload), endStream: true, default);

        // Assert
        Assert.That(result, Is.EqualTo(WriteResult.Written));
        Assert.That(decoratee.IsEnded, Is.True);
        ReadResult readResult = await pipe.Reader.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo([.. payload, .. _trailer]));

        // Cleanup
        pipe.Reader.Complete();
        await sut.CompleteAsync(default);
    }

    [Test]
    public async Task Complete_sends_the_trailing_bytes_and_completes_the_decoratee()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));
        byte[] payload = Encoding.ASCII.GetBytes("payload");
        _ = await sut.WriteAsync(new ReadOnlySequence<byte>(payload), endStream: false, default);

        // Act
        await sut.CompleteAsync(default);

        // Assert
        Assert.That(decoratee.IsCompletedSuccessfully, Is.True);
        ReadResult readResult = await pipe.Reader.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo([.. payload, .. _trailer]));
        Assert.That(readResult.IsCompleted, Is.True);

        // Cleanup
        pipe.Reader.Complete();
    }

    [Test]
    public void Abort_aborts_the_decoratee()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));

        // Act
        sut.Abort();

        // Assert
        Assert.That(decoratee.IsCompleted, Is.True);
        Assert.That(decoratee.IsCompletedSuccessfully, Is.False);
        Assert.That(
            async () => await pipe.Reader.ReadAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));

        // Cleanup
        pipe.Reader.Complete();
    }

    [Test]
    public async Task Write_returns_writes_closed_when_the_decoratee_reports_it()
    {
        // Arrange
        var pipe = new Pipe();
        var decoratee = new PipeStreamWriter(pipe.Writer);
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));
        pipe.Reader.Complete();

        // Act
        WriteResult result = await sut.WriteAsync(new ReadOnlySequence<byte>(new byte[10]), endStream: false, default);

        // Assert
        Assert.That(result, Is.EqualTo(WriteResult.WritesClosed));

        // Cleanup
        await sut.CompleteAsync(default);
    }

    [Test]
    public async Task Complete_aborts_the_decoratee_when_the_trailing_bytes_write_fails()
    {
        // Arrange
        var decoratee = new FailingStreamWriter();
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));
        _ = await sut.WriteAsync(new ReadOnlySequence<byte>(new byte[10]), endStream: false, default);
        decoratee.FailWrites = true;

        // Act/Assert
        Assert.That(async () => await sut.CompleteAsync(default), Throws.InstanceOf<IOException>());
        Assert.That(decoratee.IsCompleted, Is.True);
        Assert.That(decoratee.IsCompletedSuccessfully, Is.False);
    }

    /// <summary>Verifies that the write of the trailing bytes honors the cancellation token: a peer that stops reading
    /// cannot block the completion of the writer.</summary>
    [Test]
    public async Task Complete_is_canceled_when_the_trailing_bytes_write_blocks()
    {
        // Arrange
        var decoratee = new BlockingStreamWriter();
        var sut = new StreamTransformWriter(decoratee, stream => new TrailerStream(stream));
        _ = await sut.WriteAsync(new ReadOnlySequence<byte>(new byte[10]), endStream: false, default);
        decoratee.BlockWrites = true;
        using var cts = new CancellationTokenSource();

        // Act
        ValueTask completeTask = sut.CompleteAsync(cts.Token);
        await decoratee.WriteBlocked;
        cts.Cancel();

        // Assert
        Assert.That(async () => await completeTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(decoratee.IsCompleted, Is.True);
        Assert.That(decoratee.IsCompletedSuccessfully, Is.False);
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

    /// <summary>A write-only stream over an inner stream, which it disposes when disposed.</summary>
    private abstract class WriteOnlyStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        protected Stream Inner { get; }

        protected WriteOnlyStream(Stream inner) => Inner = inner;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>A pass-through stream that holds the bytes written to it until it's flushed or disposed.</summary>
    private sealed class BufferingStream : WriteOnlyStream
    {
        /// <summary>Gets the number of <see cref="Write(byte[], int, int)" /> calls.</summary>
        public int WriteCount { get; private set; }

        private readonly MemoryStream _buffer = new();

        public BufferingStream(Stream inner)
            : base(inner)
        {
        }

        public override void Flush()
        {
            _buffer.WriteTo(Inner);
            _buffer.SetLength(0);
            Inner.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCount++;
            _buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>A pass-through stream that writes a trailer when disposed.</summary>
    private sealed class TrailerStream : WriteOnlyStream
    {
        /// <summary>Gets the number of <see cref="Write(byte[], int, int)" /> calls.</summary>
        public int WriteCount { get; private set; }

        public TrailerStream(Stream inner)
            : base(inner)
        {
        }

        public override void Flush() => Inner.Flush();

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCount++;
            Inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Inner.Write(_trailer, 0, _trailer.Length);
            }
            base.Dispose(disposing);
        }
    }
}
