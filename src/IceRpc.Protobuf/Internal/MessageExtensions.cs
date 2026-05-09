// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.Internal;

/// <summary>Provides an extension method for <see cref="IMessage" />.</summary>
internal static class MessageExtensions
{
    /// <summary>Encodes an <see cref="IMessage"/> as a length-prefixed message, using the Protobuf encoding.</summary>
    /// <param name="message">The <see cref="IMessage" /> to encode.</param>
    /// <param name="pipeOptions">The options used to create the pipe.</param>
    /// <returns>A <see cref="PipeReader" /> containing the length-prefixed message.</returns>
    /// <remarks>The envelope follows the gRPC Length-Prefixed-Message format: a 1-byte compression flag (always
    /// 0 — IceRPC does not compress Protobuf payloads), followed by the message length as a 4-byte unsigned
    /// big-endian integer, followed by the Protobuf-encoded message bytes. See
    /// <see href="https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#data-frames" />.</remarks>
    internal static PipeReader EncodeAsLengthPrefixedMessage(this IMessage message, PipeOptions pipeOptions)
    {
        var pipe = new Pipe(pipeOptions);
        pipe.Writer.Write(new Span<byte>([0])); // Not compressed
        Span<byte> lengthPlaceholder = pipe.Writer.GetSpan(4);
        pipe.Writer.Advance(4);
        message.WriteTo(pipe.Writer);
        int length = checked((int)pipe.Writer.UnflushedBytes);
        BinaryPrimitives.WriteUInt32BigEndian(lengthPlaceholder, (uint)(length - 5));
        pipe.Writer.Complete();
        return pipe.Reader;
    }
}
