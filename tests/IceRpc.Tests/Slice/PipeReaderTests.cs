// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class PipeReaderTests
{
    [Test]
    public async Task Calling_advance_to_on_an_empty_segment()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 0 }); // empty segment
        ReadResult readResult = await pipe.Reader.ReadSegmentAsync(SliceEncoding.Slice20, default);

        pipe.Reader.AdvanceTo(readResult.Buffer.End);

        Assert.That(readResult.IsCompleted, Is.False);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Test]
    public async Task Reading_a_segment_piecemeal()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 21 }); // first byte of size "5" encoded on 2 bytes
        Task<ReadResult> task = pipe.Reader.ReadSegmentAsync(SliceEncoding.Slice20, default).AsTask();
        await pipe.Writer.WriteAsync(new byte[] { 0, 1, 2, 3 }); // remaining byte of size + 3 bytes of payload
        await Task.Yield(); // give a chance to task to run
        await pipe.Writer.WriteAsync(new byte[] { 4, 5, 123 }); // remaining bytes of payload + one extra byte
        await pipe.Writer.CompleteAsync();

        ReadResult readResult = await task;

        Assert.That(readResult.IsCompleted, Is.False);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        await pipe.Reader.CompleteAsync();
    }

    [Test]
    public void Reading_a_segment_with_an_invalid_size_fails()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC })); // invalid size

        Assert.That(
            async () => await pipeReader.ReadSegmentAsync(SliceEncoding.Slice20, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Reads an invalid segment which does not contain enough bytes.</summary>
    [Test]
    public void Reading_a_short_segment_fails()
    {
        // 20 = 4 * 5 means the payload size is 5
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 20, 1, 2, 3, 4 }));

        Assert.That(
            async () => await pipeReader.ReadSegmentAsync(SliceEncoding.Slice20, default),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task Trying_to_read_an_incomplete_segment_returns_false()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 20, 1, 2, 3, 4 });

        bool success = pipe.Reader.TryReadSegment(SliceEncoding.Slice20, out ReadResult readResult);

        Assert.That(success, Is.False);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public async Task Trying_to_read_a_complete_segment()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 20, 1, 2, 3, 4, 5 });

        bool success = pipe.Reader.TryReadSegment(SliceEncoding.Slice20, out ReadResult readResult);

        Assert.That(success, Is.True);
        Assert.That(readResult.IsCompleted, Is.False);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public void Trying_to_read_a_segment_with_an_invalid_size_fails()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC })); // invalid size

        Assert.That(
            () => _ = pipeReader.TryReadSegment(SliceEncoding.Slice20, out ReadResult readResult),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Trying_to_read_a_short_segment_fails()
    {
        // 20 = 4 * 5 means the payload size is 5
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 20, 1, 2, 3, 4 }));

        Assert.That(
            () => _ = pipeReader.TryReadSegment(SliceEncoding.Slice20, out ReadResult readResult),
            Throws.TypeOf<InvalidDataException>());
    }
}
