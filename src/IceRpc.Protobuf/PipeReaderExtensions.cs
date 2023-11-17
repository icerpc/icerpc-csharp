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
        T? message = await ReadMessageAsync(
            reader,
            parser,
            maxMessageLength,
            cancellationToken).ConfigureAwait(false);

        Debug.Assert(message is not null);
        return message;
    }

    /// <summary>Decodes an async enumerable from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="messageParser">The <see cref="MessageParser{T}" /> used to parse the message data.</param>
    /// <param name="maxMessageLength">The maximum allowed length.</param>
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

        if (readResult.Buffer.FirstSpan[0] == 1)
        {
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
