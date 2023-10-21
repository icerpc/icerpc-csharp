// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace IceRpc.Protobuf;

/// <summary>Provides an extension method for <see cref="IMessage" />.</summary>
public static class MessageExtensions
{
    /// <summary>Encodes an <see cref="IMessage"/> as a length-prefixed message, using the Protobuf encoding.</summary>
    /// <param name="message">The <see cref="IMessage" /> to encode.</param>
    /// <param name="pipeOptions">The options used to create the pipe.</param>
    /// <returns>A <see cref="PipeReader" /> containing the length-prefixed message.</returns>
    public static PipeReader EncodeAsLengthPrefixedMessage(this IMessage message, PipeOptions pipeOptions)
    {
        var pipe = new Pipe(pipeOptions);
        pipe.Writer.Write(new Span<byte>([0])); // Not compressed
        Span<byte> lengthPlaceholder = pipe.Writer.GetSpan(4);
        pipe.Writer.Advance(4);
        message.WriteTo(pipe.Writer);
        int length = checked((int)pipe.Writer.UnflushedBytes);
        BinaryPrimitives.WriteInt32BigEndian(lengthPlaceholder, length - 5);
        pipe.Writer.Complete();
        return pipe.Reader;
    }
}
