// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Protobuf.RpcMethods.Internal;

/// <summary>Provides extension methods for <see cref="PipeReader" />.</summary>
internal static class PipeReaderExtensions
{
    /// <summary>Decodes a Protobuf length-prefixed message from a <see cref="PipeReader" />.</summary>
    /// <param name="reader">The <see cref="PipeReader" /> containing the Protobuf length-prefixed message.</param>
    /// <param name="parser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The decoded message object.</returns>
    /// <remarks>The envelope follows the gRPC Length-Prefixed-Message format: a 1-byte compression flag (0 for
    /// uncompressed; 1 indicates compressed and is rejected as not supported), followed by the message length
    /// as a 4-byte unsigned big-endian integer, followed by the Protobuf-encoded message bytes. See
    /// <see href="https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#data-frames" />.</remarks>
    internal static async ValueTask<T> DecodeProtobufMessageAsync<T>(
        this PipeReader reader,
        MessageParser<T> parser,
        int maxMessageLength,
        CancellationToken cancellationToken) where T : IMessage<T>
    {
        T? message = await ReadMessageAsync(
            reader,
            parser,
            maxMessageLength,
            cancellationToken).ConfigureAwait(false);

        Debug.Assert(message is not null);

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

    /// <summary>Decodes an async enumerable from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
    /// <param name="cancellationToken">The cancellation token which is provided to <see
    /// cref="IAsyncEnumerable{T}.GetAsyncEnumerator(CancellationToken)" />.</param>
    internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader reader,
        MessageParser<T> messageParser,
        int maxMessageLength,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : IMessage<T>
    {
        try
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                T? message;
                try
                {
                    message = await ReadMessageAsync(
                        reader,
                        messageParser,
                        maxMessageLength,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                {
                    // Canceling the cancellation token is a normal way to complete an iteration.
                    yield break;
                }

                if (message is null)
                {
                    yield break;
                }
                yield return message;
            }
        }
        finally
        {
            reader.Complete();
        }
    }

    private static async ValueTask<T?> ReadMessageAsync<T>(
        PipeReader reader,
        MessageParser<T> messageParser,
        int maxMessageLength,
        CancellationToken cancellationToken) where T : IMessage<T>
    {
        ReadResult readResult = await reader.ReadAtLeastAsync(5, cancellationToken).ConfigureAwait(false);
        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        if (readResult.Buffer.IsEmpty)
        {
            return default;
        }

        if (readResult.Buffer.Length < 5)
        {
            throw new InvalidDataException(
                $"The payload has {readResult.Buffer.Length} bytes, but 5 bytes were expected.");
        }

        byte compressionFlag = readResult.Buffer.FirstSpan[0];
        switch (compressionFlag)
        {
            case 0:
                break;
            case 1:
                // The Protobuf envelope defines flag 1 as "compressed". The message is well-formed Protobuf,
                // but IceRPC doesn't decompress it.
                throw new NotSupportedException("IceRPC does not support Protobuf compressed messages.");
            default:
                // The Protobuf envelope only defines flags 0 and 1; any other value is malformed.
                throw new InvalidDataException(
                    $"Invalid Protobuf compression flag {compressionFlag}; expected 0 or 1.");
        }
        int messageLength = DecodeMessageLength(readResult.Buffer.Slice(1, 4), maxMessageLength);
        reader.AdvanceTo(readResult.Buffer.GetPosition(5));

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

        T message;
        try
        {
            // ParseFrom reads tags until end-of-input and throws InvalidProtocolBufferException on a
            // truncated field, so passing an exact-length slice means it either consumes every byte or
            // throws.
            message = messageParser.ParseFrom(readResult.Buffer.Slice(0, messageLength));
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new InvalidDataException("Failed to decode the Protobuf message.", exception);
        }
        reader.AdvanceTo(readResult.Buffer.GetPosition(messageLength));
        return message;

        static int DecodeMessageLength(ReadOnlySequence<byte> buffer, int maxMessageLength)
        {
            Debug.Assert(buffer.Length == 4);
            Span<byte> spanBuffer = stackalloc byte[4];
            buffer.CopyTo(spanBuffer);
            // The Protobuf envelope encodes the length as an unsigned 32-bit big-endian integer. The cast of
            // maxMessageLength to uint is safe because it is a non-negative int, and covers both the > int.MaxValue
            // case and the > maxMessageLength case in a single comparison.
            uint messageLength = BinaryPrimitives.ReadUInt32BigEndian(spanBuffer);
            if (messageLength > (uint)maxMessageLength)
            {
                throw new InvalidDataException("The message length exceeds the maximum value.");
            }
            return (int)messageLength;
        }
    }
}
