// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public class PipeWriterExtensionsTests
{
    [Test]
    public void PipeWriter_complete_output_completes_the_pipe_writer([Values] bool success)
    {
        // Arrange
        var pipe = new Pipe();
        var writer = new PayloadPipeWriterDecorator(pipe.Writer);

        // Act
        writer.CompleteOutput(success);

        // Assert
        Assert.That(writer.Completed.Result, success ? Is.Null : Is.InstanceOf<Exception>());
        pipe.Reader.Complete();
    }

    [Test]
    public async Task PipeWriter_copy_from_with_completed_source_does_not_complete_the_writer()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var sourcePipe = new Pipe();
        sourcePipe.Writer.Complete();
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        _ = await destinationPipe.Writer.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        Assert.That(destinationPipe.Reader.TryRead(out ReadResult _), Is.False);

        sourcePipe.Reader.Complete();
        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
    }

    [Test]
    public async Task PipeWriter_copy_from_with_canceled_source_pipe_reader()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var sourcePipe = new Pipe();
        sourcePipe.Reader.CancelPendingRead();
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        FlushResult flushResult = await destinationPipe.Writer.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        Assert.That(flushResult.IsCompleted, Is.True);
        Assert.That(flushResult.IsCanceled, Is.True);

        sourcePipe.Reader.Complete();
        sourcePipe.Writer.Complete();
        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
    }

    [Test]
    public async Task PipeWriter_copy_from_with_canceled_pipe_writer()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var sourcePipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[1]));
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        destinationPipe.Writer.CancelPendingFlush();

        // Assert
        FlushResult flushResult = await destinationPipe.Writer.CopyFromAsync(
            sourcePipeReader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);
        Assert.That(flushResult.IsCompleted, Is.False);
        Assert.That(flushResult.IsCanceled, Is.True);

        sourcePipeReader.Complete();
        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
    }

    [Test]
    public async Task PipeWriter_copy_from_read_canceled_on_write_closed_task_completion()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var sourcePipe = new Pipe();
        var writesClosedTcs = new TaskCompletionSource();

        ValueTask<FlushResult> flushResultTask = destinationPipe.Writer.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);
        bool flushResultTaskCompletedBeforeCancel = flushResultTask.IsCompleted;

        // Act
        writesClosedTcs.SetResult();

        // Assert
        Assert.That(flushResultTaskCompletedBeforeCancel, Is.False);
        FlushResult flushResult = await flushResultTask;
        Assert.That(flushResult.IsCompleted, Is.False);
        Assert.That(flushResult.IsCanceled, Is.False);

        sourcePipe.Writer.Complete();
        sourcePipe.Reader.Complete();
        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
    }

    [Test]
    public async Task PipeWriter_copy_from_with_end_stream_completes_the_writer(
        [Values] bool withReadOnlySequencePipeWriter)
    {
        // Arrange
        var destinationPipe = new Pipe();
        PipeWriter destinationWriter = destinationPipe.Writer;
        if (withReadOnlySequencePipeWriter)
        {
            destinationWriter = new ReadOnlySequencePipeWriterDecorator(destinationWriter);
        }
        var sourceReader = PipeReader.Create(ReadOnlySequence<byte>.Empty);
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        _ = await destinationWriter.CopyFromAsync(
            sourceReader,
            writesClosedTcs.Task,
            endStream: true,
            CancellationToken.None);

        // Assert
        ReadResult readResult = await destinationPipe.Reader.ReadAsync();
        Assert.That(readResult.IsCompleted, Is.True);
        if (destinationWriter is ReadOnlySequencePipeWriterDecorator readOnlySequencePipeWriter)
        {
            Assert.That(readOnlySequencePipeWriter.ReadOnlySequenceWriteCalled, Is.True);
        }

        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
        sourceReader.Complete();
    }

    [Test]
    public async Task PipeWriter_copy_from_reader_with_data([Values] bool withReadOnlySequencePipeWriter)
    {
        // Arrange
        var destinationPipe = new Pipe();
        PipeWriter destinationWriter = destinationPipe.Writer;
        if (withReadOnlySequencePipeWriter)
        {
            destinationWriter = new ReadOnlySequencePipeWriterDecorator(destinationWriter);
        }

        var sourcePipe = new Pipe();
        sourcePipe.Writer.Write(new byte[10]);
        sourcePipe.Writer.Write(new byte[10]);
        sourcePipe.Writer.Complete();
        _ = await sourcePipe.Writer.FlushAsync();

        var writesClosedTcs = new TaskCompletionSource();

        // Act
        _ = await destinationWriter.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        ReadResult readResult = await destinationPipe.Reader.ReadAsync();
        Assert.That(readResult.Buffer.Length, Is.EqualTo(20));
        if (destinationWriter is ReadOnlySequencePipeWriterDecorator readOnlySequencePipeWriter)
        {
            Assert.That(readOnlySequencePipeWriter.ReadOnlySequenceWriteCalled, Is.True);
        }

        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
        sourcePipe.Writer.Complete();
        sourcePipe.Reader.Complete();
    }

    private class ReadOnlySequencePipeWriterDecorator : ReadOnlySequencePipeWriter
    {
        internal bool ReadOnlySequenceWriteCalled { get; private set; }

        private readonly PipeWriter _decoratee;

        public override void Advance(int bytes) => _decoratee.Advance(bytes);

        public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception = null) => _decoratee.Complete();

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
            _decoratee.FlushAsync(cancellationToken);

        public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancellationToken)
        {
            ReadOnlySequenceWriteCalled = true;
            this.Write(source);
            if (endStream)
            {
                Complete(null);
            }
            return FlushAsync(cancellationToken);
        }

        internal ReadOnlySequencePipeWriterDecorator(PipeWriter decoratee) => _decoratee = decoratee;
    }
}
