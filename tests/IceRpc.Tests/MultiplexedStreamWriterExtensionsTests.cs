// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public class MultiplexedStreamWriterExtensionsTests
{
    [Test]
    public async Task Copy_from_with_completed_source_does_not_complete_the_writer()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var destinationWriter = new PipeStreamWriter(destinationPipe.Writer);
        var sourcePipe = new Pipe();
        sourcePipe.Writer.Complete();
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        CopyResult copyResult = await destinationWriter.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        Assert.That(copyResult, Is.EqualTo(CopyResult.Completed));
        Assert.That(destinationWriter.IsCompleted, Is.False);
        Assert.That(destinationPipe.Reader.TryRead(out ReadResult _), Is.False);

        sourcePipe.Reader.Complete();
        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
    }

    [Test]
    public async Task Copy_from_with_canceled_source_pipe_reader()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var destinationWriter = new PipeStreamWriter(destinationPipe.Writer);
        var sourcePipe = new Pipe();
        sourcePipe.Reader.CancelPendingRead();
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        CopyResult copyResult = await destinationWriter.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        Assert.That(copyResult, Is.EqualTo(CopyResult.Canceled));

        sourcePipe.Reader.Complete();
        sourcePipe.Writer.Complete();
        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
    }

    [Test]
    public async Task Copy_from_reports_writes_closed_when_the_destination_reader_is_completed()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var destinationWriter = new PipeStreamWriter(destinationPipe.Writer);
        destinationPipe.Reader.Complete();
        var sourcePipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[1]));
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        CopyResult copyResult = await destinationWriter.CopyFromAsync(
            sourcePipeReader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        Assert.That(copyResult, Is.EqualTo(CopyResult.WritesClosed));

        sourcePipeReader.Complete();
        destinationPipe.Writer.Complete();
    }

    [Test]
    public async Task Copy_from_read_canceled_on_writes_closed_task_completion()
    {
        // Arrange
        var destinationPipe = new Pipe();
        destinationPipe.Reader.Complete(); // the writes closed task completes when the peer stops reading
        var destinationWriter = new PipeStreamWriter(destinationPipe.Writer);
        var sourcePipe = new Pipe();
        var writesClosedTcs = new TaskCompletionSource();

        ValueTask<CopyResult> copyResultTask = destinationWriter.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);
        bool copyResultTaskCompletedBeforeCancel = copyResultTask.IsCompleted;

        // Act
        writesClosedTcs.SetResult();

        // Assert
        Assert.That(copyResultTaskCompletedBeforeCancel, Is.False);
        Assert.That(await copyResultTask, Is.EqualTo(CopyResult.WritesClosed));

        sourcePipe.Writer.Complete();
        sourcePipe.Reader.Complete();
        destinationPipe.Writer.Complete();
    }

    [Test]
    public async Task Copy_from_with_end_stream_ends_the_destination()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var destinationWriter = new PipeStreamWriter(destinationPipe.Writer);
        var sourceReader = PipeReader.Create(ReadOnlySequence<byte>.Empty);
        var writesClosedTcs = new TaskCompletionSource();

        // Act
        CopyResult copyResult = await destinationWriter.CopyFromAsync(
            sourceReader,
            writesClosedTcs.Task,
            endStream: true,
            CancellationToken.None);

        // Assert
        Assert.That(copyResult, Is.EqualTo(CopyResult.Completed));
        Assert.That(destinationWriter.IsEnded, Is.True);

        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
        sourceReader.Complete();
    }

    [Test]
    public async Task Copy_from_reader_with_data()
    {
        // Arrange
        var destinationPipe = new Pipe();
        var destinationWriter = new PipeStreamWriter(destinationPipe.Writer);

        var sourcePipe = new Pipe();
        sourcePipe.Writer.Write(new byte[10]);
        sourcePipe.Writer.Write(new byte[10]);
        sourcePipe.Writer.Complete();

        var writesClosedTcs = new TaskCompletionSource();

        // Act
        CopyResult copyResult = await destinationWriter.CopyFromAsync(
            sourcePipe.Reader,
            writesClosedTcs.Task,
            endStream: false,
            CancellationToken.None);

        // Assert
        Assert.That(copyResult, Is.EqualTo(CopyResult.Completed));
        ReadResult readResult = await destinationPipe.Reader.ReadAsync();
        Assert.That(readResult.Buffer.Length, Is.EqualTo(20));

        destinationPipe.Writer.Complete();
        destinationPipe.Reader.Complete();
        sourcePipe.Writer.Complete();
        sourcePipe.Reader.Complete();
    }
}
