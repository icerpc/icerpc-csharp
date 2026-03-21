// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.None.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    [Test]
    public void Decode_bool_sequence_field()
    {
        // Arrange
        bool[] expected = [false, true, false];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x00 });
        var decoder = new IceDecoder(buffer.WrittenMemory);

        // Act
        var sut = new BoolS(ref decoder);

        // Assert
        Assert.That(sut.Value, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_bool_sequence_field_with_invalid_values()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x02 });

        // Act/Assert
        Assert.Throws<InvalidDataException>(
            () =>
            {
                var decoder = new IceDecoder(buffer.WrittenMemory);
                _ = new BoolS(ref decoder);
            });
    }
}
