// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Operations.Internal;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Ice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class PipeReaderExtensionsTests
{
    [Test]
    public async Task Reading_a_full_payload_with_size_equal_to_max_size_succeeds()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }));

        ReadResult readResult = await pipeReader.ReadFullPayloadAsync(maxSize: 5, default);

        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        pipeReader.Complete();
    }

    [Test]
    public void Reading_a_full_payload_larger_than_max_size_fails()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }));

        Assert.That(
            async () => await pipeReader.ReadFullPayloadAsync(maxSize: 4, default),
            Throws.TypeOf<InvalidDataException>());
        pipeReader.Complete();
    }

    [Test]
    public void Trying_to_read_a_full_payload_with_size_equal_to_max_size_succeeds()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }));

        bool success = pipeReader.TryReadFullPayload(maxSize: 5, out ReadResult readResult);

        Assert.That(success, Is.True);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        pipeReader.Complete();
    }

    [Test]
    public void Trying_to_read_a_full_payload_larger_than_max_size_fails()
    {
        var pipeReader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }));

        Assert.That(
            () => _ = pipeReader.TryReadFullPayload(maxSize: 4, out ReadResult readResult),
            Throws.TypeOf<InvalidDataException>());
        pipeReader.Complete();
    }
}
