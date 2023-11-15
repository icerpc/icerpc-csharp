// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class PipeReaderExtensionsTests
{
    [Test]
    public void Read_message_throws_invalid_data_exception_when_max_message_lenght_is_exceeded()
    {
        // Arrange
        var stringValue = new StringValue();
        stringValue.Value = new string('s', 1024);
        var pipeReader = stringValue.EncodeAsLengthPrefixedMessage(new PipeOptions(pauseWriterThreshold: 0));

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await pipeReader.DecodeProtobufMessageAsync(
                StringValue.Parser,
                maxMessageLength: 1024,
                CancellationToken.None));
        pipeReader.Complete();
    }
}
