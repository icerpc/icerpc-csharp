// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using IceRpc.Protobuf.RpcMethods.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class PipeReaderExtensionsTests
{
    [Test]
    public async Task Read_message_succeeds_when_message_length_equals_max_message_length()
    {
        // Arrange
        // A StringValue with 1024 ASCII chars encodes to 1 (tag) + 2 (varint length) + 1024 (data) = 1027 bytes.
        var stringValue = new StringValue { Value = new string('s', 1024) };
        var pipeReader = stringValue.EncodeAsLengthPrefixedMessage(new PipeOptions(pauseWriterThreshold: 0));

        // Act
        StringValue decoded = await pipeReader.DecodeProtobufMessageAsync(
            StringValue.Parser,
            maxMessageLength: 1027,
            CancellationToken.None);

        // Assert
        Assert.That(decoded.Value, Is.EqualTo(stringValue.Value));
        pipeReader.Complete();
    }

    [Test]
    public void Read_message_throws_invalid_data_exception_when_max_message_length_is_exceeded()
    {
        // Arrange
        // A StringValue with 1024 ASCII chars encodes to 1027 bytes; 1026 is one byte short of the limit.
        var stringValue = new StringValue { Value = new string('s', 1024) };
        var pipeReader = stringValue.EncodeAsLengthPrefixedMessage(new PipeOptions(pauseWriterThreshold: 0));

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipeReader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1026,
                CancellationToken.None));
        pipeReader.Complete();
    }

    [Test]
    public void Decode_message_throws_invalid_data_exception_when_payload_has_trailing_bytes()
    {
        // Arrange
        var pipe = new Pipe();
        WriteLengthPrefixedMessage(pipe.Writer, new StringValue { Value = "hello" });
        pipe.Writer.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        pipe.Writer.Complete();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipe.Reader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        pipe.Reader.Complete();
    }

    [Test]
    public void Decode_message_throws_invalid_data_exception_when_payload_has_concatenated_messages()
    {
        // Arrange
        var pipe = new Pipe();
        WriteLengthPrefixedMessage(pipe.Writer, new StringValue { Value = "hello" });
        WriteLengthPrefixedMessage(pipe.Writer, new StringValue { Value = "world" });
        pipe.Writer.Complete();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipe.Reader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        pipe.Reader.Complete();
    }

    /// <summary>Verifies that a Protobuf message with the "compressed" flag (1) is rejected as
    /// <see cref="NotSupportedException" />. The message is well-formed Protobuf, but IceRPC doesn't
    /// decompress it. The protocol layer maps this to <c>StatusCode.NotSupported</c>.</summary>
    [Test]
    public void Decode_message_throws_not_supported_exception_for_compressed_flag()
    {
        // Arrange
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 1 });
        Span<byte> lengthBytes = pipe.Writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, 0);
        pipe.Writer.Advance(4);
        pipe.Writer.Complete();

        // Act & Assert
        Assert.ThrowsAsync<NotSupportedException>(async () =>
            await pipe.Reader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        pipe.Reader.Complete();
    }

    /// <summary>Verifies that a Protobuf message with a reserved compression flag value (anything other than
    /// 0 or 1) is rejected as <see cref="InvalidDataException" />. Prior to the fix, values 2..255 were
    /// silently accepted as if uncompressed.</summary>
    [TestCase((byte)2)]
    [TestCase((byte)0xFF)]
    public void Decode_message_throws_invalid_data_exception_for_reserved_compression_flag(byte compressionFlag)
    {
        // Arrange
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { compressionFlag });
        Span<byte> lengthBytes = pipe.Writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, 0);
        pipe.Writer.Advance(4);
        pipe.Writer.Complete();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipe.Reader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        pipe.Reader.Complete();
    }

    /// <summary>Verifies that a length whose high bit is set (decoded as a negative <see cref="int" />) is
    /// rejected as <see cref="InvalidDataException" /> before reaching <c>ReadAtLeastAsync</c>, which would
    /// otherwise throw <see cref="ArgumentOutOfRangeException" />.</summary>
    [Test]
    public void Decode_message_throws_invalid_data_exception_for_negative_message_length()
    {
        // Arrange
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 0 });
        // 0xFFFFFFFF as big-endian — decodes to -1 as Int32.
        pipe.Writer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        pipe.Writer.Complete();

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipe.Reader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        pipe.Reader.Complete();
    }

    /// <summary>Verifies that a tampered envelope whose length field claims more bytes than the actual
    /// message contains is rejected as <see cref="InvalidDataException" />. We slice <c>ParseFrom</c>'s
    /// input to exactly the envelope's claimed length, so trailing malformed bytes inside the envelope
    /// cause <c>ParseFrom</c> to throw <see cref="InvalidProtocolBufferException" />, which we wrap as
    /// <see cref="InvalidDataException" /> so the protocol layer maps it to <c>StatusCode.InvalidData</c>.
    /// </summary>
    [Test]
    public void Decode_message_throws_invalid_data_exception_for_envelope_length_overrun()
    {
        // Arrange: serialize a valid StringValue, claim one extra byte in the envelope length, and append a
        // single 0xFF — a varint with the continuation bit set and no follow-up byte, which Protobuf treats
        // as a truncated tag.
        var validMessage = new StringValue { Value = "hi" };
        int actualLength = validMessage.CalculateSize();

        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 0 });
        Span<byte> lengthBytes = pipe.Writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, actualLength + 1);
        pipe.Writer.Advance(4);
        validMessage.WriteTo(pipe.Writer);
        pipe.Writer.Write(new byte[] { 0xFF });
        pipe.Writer.Complete();

        // Act & Assert
        InvalidDataException? exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipe.Reader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        Assert.That(exception!.InnerException, Is.InstanceOf<InvalidProtocolBufferException>());
        pipe.Reader.Complete();
    }

    private static void WriteLengthPrefixedMessage(PipeWriter writer, IMessage message)
    {
        writer.Write(new byte[] { 0 }); // Not compressed
        Span<byte> lengthBytes = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, message.CalculateSize());
        writer.Advance(4);
        message.WriteTo(writer);
    }
}
