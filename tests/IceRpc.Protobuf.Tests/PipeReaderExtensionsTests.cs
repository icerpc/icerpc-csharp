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

    private static void WriteLengthPrefixedMessage(PipeWriter writer, IMessage message)
    {
        writer.Write(new byte[] { 0 }); // Not compressed
        Span<byte> lengthBytes = writer.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, message.CalculateSize());
        writer.Advance(4);
        message.WriteTo(writer);
    }
}
