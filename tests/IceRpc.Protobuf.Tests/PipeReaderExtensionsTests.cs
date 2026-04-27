// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Protobuf.RpcMethods.Internal;
using NUnit.Framework;
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
}
