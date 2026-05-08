// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.RpcMethods.Internal;

/// <summary>Provides extension methods for <see cref="PipeReader" />.</summary>
internal static class PipeReaderExtensions
{
    /// <summary>Decodes a Protobuf length prefixed message from a <see cref="PipeReader" />.</summary>
    /// <param name="reader">The <see cref="PipeReader" /> containing the Protobuf length prefixed message.</param>
    /// <param name="parser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The decoded message object.</returns>
    internal static async ValueTask<T> DecodeProtobufMessageAsync<T>(
        this PipeReader reader,
        MessageParser<T> parser,
        int maxMessageLength,
        CancellationToken cancellationToken) where T : class, IMessage<T>
    {
        T message = await reader.ReadProtobufMessageAsync(
            parser,
            maxMessageLength,
            cancellationToken).ConfigureAwait(false) ??
            throw new InvalidDataException("The payload is empty; expected a Protobuf message.");

        // A unary payload must contain exactly one message; any trailing bytes indicate a framing error.
        ReadResult readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }
        bool hasTrailingBytes = !readResult.Buffer.IsEmpty;
        reader.AdvanceTo(readResult.Buffer.End);
        if (hasTrailingBytes)
        {
            throw new InvalidDataException(
                "The payload contains unexpected trailing bytes after the Protobuf message.");
        }
        Debug.Assert(readResult.IsCompleted);

        return message;
    }

    /// <summary>Creates an async stream over a pipe reader to decode Protobuf messages.</summary>
    /// <typeparam name="T">The type of the message being decoded.</typeparam>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
    /// <returns>The async stream to decode and return the streamed messages.</returns>
    /// <remarks>The reader ownership is transferred to the returned async stream. The caller should no longer use
    /// the reader after this call, and must dispose the returned async stream when done to release the reader.
    /// </remarks>
    internal static IAsyncStream<T> ToAsyncStream<T>(
        this PipeReader reader,
        MessageParser<T> messageParser,
        int maxMessageLength) where T : class, IMessage<T> =>
        new AsyncStream<T>(reader, messageParser, maxMessageLength);

    /// <summary>Reads a single Protobuf length-prefixed message from a <see cref="PipeReader" />.</summary>
    /// <typeparam name="T">The type of the message being decoded.</typeparam>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The decoded message, or <see langword="null" /> when the reader's stream is empty (end of stream).
    /// The <see langword="null" /> return value is used as a sentinel to signal end of stream; this is why
    /// <typeparamref name="T" /> is constrained to be a reference type.</returns>
    internal static async ValueTask<T?> ReadProtobufMessageAsync<T>(
        this PipeReader reader,
        MessageParser<T> messageParser,
        int maxMessageLength,
        CancellationToken cancellationToken) where T : class, IMessage<T>
    {
        ReadResult readResult = await reader.ReadAtLeastAsync(5, cancellationToken).ConfigureAwait(false);
        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        if (readResult.Buffer.IsEmpty)
        {
            return null;
        }

        if (readResult.Buffer.Length < 5)
        {
            throw new InvalidDataException(
                $"The payload has {readResult.Buffer.Length} bytes, but 5 bytes were expected.");
        }

        if (readResult.Buffer.FirstSpan[0] == 1)
        {
            throw new NotSupportedException("IceRPC does not support Protobuf compressed messages");
        }
        int messageLength = DecodeMessageLength(readResult.Buffer.Slice(1, 4));
        reader.AdvanceTo(readResult.Buffer.GetPosition(5));
        if (messageLength > maxMessageLength)
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
            throw new InvalidDataException(
                $"The payload has {readResult.Buffer.Length} bytes, but {messageLength} bytes were expected.");
        }

        // TODO: Does ParseFrom check it read all the bytes?
        T message = messageParser.ParseFrom(readResult.Buffer.Slice(0, messageLength));
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
