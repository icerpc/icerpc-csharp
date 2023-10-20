// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace IceRpc.Protobuf;

/// <summary>Provides an extension method for <see cref="IMessage" />.</summary>
public static class MessageExtensions
{
    /// <summary>Encodes an <see cref="IMessage" /> as a Protobuf length prefixed message into a
    /// <see cref="PipeReader" />.</summary>
    /// <param name="message">The <see cref="IMessage" /> to encode.</param>
    /// <param name="pipeOptions">The options used to create the pipe.</param>
    /// <returns>The <see cref="PipeReader" /> containing the protobuf-encoded data.</returns>
    public static PipeReader EncodeAsLengthPrefixedMessage(this IMessage message, PipeOptions pipeOptions)
    {
        var pipe = new Pipe(pipeOptions);
        Span<byte> compressed = [0];
        pipe.Writer.Write(compressed); // Not compressed
        Span<byte> lengthPlaceholder = pipe.Writer.GetSpan(4);
        pipe.Writer.Advance(4);
        message.WriteTo(pipe.Writer);
        int length = checked((int)pipe.Writer.UnflushedBytes);
        BinaryPrimitives.WriteInt32BigEndian(lengthPlaceholder, length - 5);
        pipe.Writer.Complete();
        return pipe.Reader;
    }
}
