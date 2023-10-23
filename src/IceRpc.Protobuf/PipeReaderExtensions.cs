// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Protobuf;

/// <summary>Provides an extension method for <see cref="PipeReader" />.</summary>
public static class MessageParserExtensions
{
    /// <summary>Decodes a Protobuf length prefixed message from a <see cref="PipeReader" />.</summary>
    /// <param name="reader">The <see cref="PipeReader" /> containing the Protobuf length prefixed message.</param>
    /// <param name="parser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The decoded message object.</returns>
    public static async ValueTask<T> DecodeProtobufMessageAsync<T>(
        this PipeReader reader,
        MessageParser<T> parser,
        int maxMessageLength,
        CancellationToken cancellationToken) where T : IMessage<T>
    {
        ReadResult readResult = await reader.ReadAtLeastAsync(5, cancellationToken).ConfigureAwait(false);
        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        if (readResult.Buffer.Length < 5)
        {
            throw new InvalidDataException(
                $"The payload has {readResult.Buffer.Length} bytes, but 5 bytes were expected.");
        }

        if (readResult.Buffer.FirstSpan[0] == 1)
        {
            reader.AdvanceTo(readResult.Buffer.GetPosition(5));
            throw new NotSupportedException("IceRPC does not support Protobuf compressed messages");
        }
        int messageLength = DecodeMessageLength(readResult.Buffer.Slice(1, 4));
        reader.AdvanceTo(readResult.Buffer.GetPosition(5));
        if (messageLength >= maxMessageLength)
        {
            throw new InvalidDataException("The message length exceeds the maximum value.");
        }

        readResult = await reader.ReadAtLeastAsync(messageLength, cancellationToken).ConfigureAwait(false);
        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        if (readResult.Buffer.Length < messageLength)
        {
            reader.AdvanceTo(readResult.Buffer.End);
            throw new InvalidDataException(
                $"The payload has {readResult.Buffer.Length} bytes, but {messageLength} bytes were expected.");
        }

        // TODO: Does ParseFrom check it read all the bytes?
        T message = parser.ParseFrom(readResult.Buffer.Slice(messageLength));
        reader.AdvanceTo(readResult.Buffer.GetPosition(messageLength));
        return message;

        static int DecodeMessageLength(ReadOnlySequence<byte> buffer)
        {
            Debug.Assert(buffer.Length == 4);
            Span<byte> spanBuffer = stackalloc byte[4];
            buffer.CopyTo(spanBuffer);
            return BinaryPrimitives.ReadInt32BigEndian(spanBuffer);
        }
    }
}
