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
    public void Read_segment_with_invalid_size_fails()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC })); // invalid size

        Assert.That(
            async () => await pipeReader.ReadSegmentAsync(Encoding.Slice20, default),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Try_read_segment_with_invalid_size_fails()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 0xAA, 0xBB, 0xCC })); // invalid size

        Assert.That(
            () => _ = pipeReader.TryReadSegment(Encoding.Slice20, out ReadResult readResult),
            Throws.InstanceOf<InvalidDataException>());
    }
}
