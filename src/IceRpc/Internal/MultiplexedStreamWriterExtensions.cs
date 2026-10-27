// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>The result of <see cref="MultiplexedStreamWriterExtensions.CopyFromAsync" />.</summary>
internal enum CopyResult
{
    /// <summary>The reader was fully copied.</summary>
    Completed,

    /// <summary>The peer closed its reads: the copy stopped and the remaining bytes were not read.</summary>
    WritesClosed,

    /// <summary>The reader was canceled with <see cref="PipeReader.CancelPendingRead" />: the copy stopped.</summary>
    Canceled,
}

internal static class MultiplexedStreamWriterExtensions
{
    /// <summary>Copies a reader into this writer.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="reader">The reader to copy.</param>
    /// <param name="writesClosed">A task that completes when the peer closes its reads; its completion cancels a
    /// pending read of <paramref name="reader" />.</param>
    /// <param name="endStream">When <see langword="true" />, the last write ends the stream.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The copy result.</returns>
    internal static async ValueTask<CopyResult> CopyFromAsync(
        this IMultiplexedStreamWriter writer,
        PipeReader reader,
        Task writesClosed,
        bool endStream,
        CancellationToken cancellationToken)
    {
        CopyResult copyResult;
        if (reader.TryRead(out ReadResult readResult))
        {
            // We optimize for the very common situation where the all the reader bytes are available.
            copyResult = await WriteReadResultAsync().ConfigureAwait(false);

            if (readResult.IsCompleted || copyResult != CopyResult.Completed)
            {
                return copyResult;
            }
        }

        // If the peer is no longer reading, we want to cancel the reading of the payload.
        CancellationToken readToken = writesClosed.AsCancellationToken(cancellationToken);

        do
        {
            try
            {
                readResult = await reader.ReadAsync(readToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == readToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Debug.Assert(writesClosed.IsCompleted);

                // This empty write throws when the writer failed; its result doesn't matter since writesClosed is
                // completed.
                _ = await writer.WriteAsync(
                    ReadOnlySequence<byte>.Empty,
                    endStream: false,
                    CancellationToken.None).ConfigureAwait(false);
                return CopyResult.WritesClosed;
            }
            // we let other exceptions thrown by ReadAsync (including possibly an OperationCanceledException
            // thrown incorrectly) escape.

            copyResult = await WriteReadResultAsync().ConfigureAwait(false);
        }
        while (!readResult.IsCompleted && copyResult == CopyResult.Completed);

        return copyResult;

        async ValueTask<CopyResult> WriteReadResultAsync()
        {
            if (readResult.IsCanceled)
            {
                // The application (or an interceptor/middleware) called CancelPendingRead on reader.
                reader.AdvanceTo(readResult.Buffer.Start); // Did not consume any byte in reader.
                return CopyResult.Canceled;
            }
            else
            {
                try
                {
                    WriteResult writeResult = await writer.WriteAsync(
                        readResult.Buffer,
                        readResult.IsCompleted && endStream,
                        cancellationToken).ConfigureAwait(false);
                    return writeResult == WriteResult.Written ? CopyResult.Completed : CopyResult.WritesClosed;
                }
                finally
                {
                    reader.AdvanceTo(readResult.Buffer.End);
                }
            }
        }
    }
}
