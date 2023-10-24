// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Protobuf;

/// <summary>Provides extension methods for <see cref="PipeReader" />.</summary>
public static class PipeReaderExtensions
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
        (int messageLength, ReadResult readResult) = await ReadMessageAsync(
            reader,
            maxMessageLength,
            cancellationToken).ConfigureAwait(false);

        // TODO: Does ParseFrom check it read all the bytes?
        T message = parser.ParseFrom(readResult.Buffer.Slice(0, messageLength));
        reader.AdvanceTo(readResult.Buffer.GetPosition(messageLength));
        return message;
    }

    /// <summary>Decodes an async enumerable from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="messageParser">The function used to decode an element.</param>
    /// <param name="maxMessageLength"></param>
    /// <param name="cancellationToken">The cancellation token which is provided to <see
    /// cref="IAsyncEnumerable{T}.GetAsyncEnumerator(CancellationToken)" />.</param>
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
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

                ReadResult readResult;
                int messageLength;

                try
                {
                    (messageLength, readResult) = await ReadMessageAsync(
                        reader,
                        maxMessageLength,
                        cancellationToken).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
                        throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
                    }
                    if (messageLength == -1)
                    {
                        Debug.Assert(readResult.IsCompleted);
                        yield break;
                    }
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                {
                    // Canceling the cancellation token is a normal way to complete an iteration.
                    yield break;
                }

                // TODO: Does ParseFrom check it read all the bytes?
                yield return messageParser.ParseFrom(readResult.Buffer.Slice(0, messageLength));
                reader.AdvanceTo(readResult.Buffer.GetPosition(messageLength));
            }
        }
        finally
        {
            reader.Complete();
        }
    }

    private static async ValueTask<(int MessageLength, ReadResult ReadResult)> ReadMessageAsync(
        PipeReader reader,
        int maxMessageLength,
        CancellationToken cancellationToken)
    {
        ReadResult readResult = await reader.ReadAtLeastAsync(5, cancellationToken).ConfigureAwait(false);
        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
        }

        if (readResult.Buffer.IsEmpty)
        {
            return (-1, readResult);
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
            throw new InvalidDataException(
                $"The payload has {readResult.Buffer.Length} bytes, but {messageLength} bytes were expected.");
        }
        return (messageLength, readResult);

        static int DecodeMessageLength(ReadOnlySequence<byte> buffer)
        {
            Debug.Assert(buffer.Length == 4);
            Span<byte> spanBuffer = stackalloc byte[4];
            buffer.CopyTo(spanBuffer);
            return BinaryPrimitives.ReadInt32BigEndian(spanBuffer);
        }
    }
}
