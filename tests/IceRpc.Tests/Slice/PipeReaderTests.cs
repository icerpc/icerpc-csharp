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
    public async Task Call_advance_to_on_empty_segment()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 0 }); // empty segment
        ReadResult readResult = await pipe.Reader.ReadSegmentAsync(Encoding.Slice20, default);

        pipe.Reader.AdvanceTo(readResult.Buffer.End);

        Assert.That(readResult.IsCompleted, Is.False);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Test]
    public void Read_segment_with_invalid_size_throws_exception()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC })); // invalid size

        Assert.That(
            async () => await pipeReader.ReadSegmentAsync(Encoding.Slice20, default),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public async Task Read_segment_piecemeal()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 21 }); // first byte of size "5" encoded on 2 bytes
        Task<ReadResult> task = pipe.Reader.ReadSegmentAsync(Encoding.Slice20, default).AsTask();
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
    public void Read_short_segment_throws_exception()
    {
        // 20 = 4 * 5 means the payload size is 5
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 20, 1, 2, 3, 4 }));

        Assert.That(
            async () => await pipeReader.ReadSegmentAsync(Encoding.Slice20, default),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public async Task Try_read_good_segment()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 20, 1, 2, 3, 4, 5 });

        bool success = pipe.Reader.TryReadSegment(Encoding.Slice20, out ReadResult readResult);

        Assert.That(success, Is.True);
        Assert.That(readResult.IsCompleted, Is.False);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Test]
    public void Try_read_segment_with_invalid_size_throws_exception()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC })); // invalid size

        Assert.That(
            () => _ = pipeReader.TryReadSegment(Encoding.Slice20, out ReadResult readResult),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Try_read_short_segment_throws_exception()
    {
        // 20 = 4 * 5 means the payload size is 5
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 20, 1, 2, 3, 4 }));

        Assert.That(
            () => _ = pipeReader.TryReadSegment(Encoding.Slice20, out ReadResult readResult),
            Throws.InstanceOf<InvalidDataException>());
    }
}
