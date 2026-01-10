// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal static class PipeWriterExtensions
{
    private static readonly Exception _outputCompleteException = new();

    /// <summary>Extension methods for <see cref="PipeWriter" />.</summary>
    /// <param name="writer">The output (a pipe writer).</param>
    extension(PipeWriter writer)
    {
        /// <summary>Completes the output provided by a <see cref="IMultiplexedStream" />.</summary>
        /// <param name="success">When <see langword="true" />, the output is completed with a
        /// <see langword="null" /> exception. Otherwise, it's completed with an exception. The exception used
        /// does not matter since Output behaves the same when completed with any exception.</param>
        internal void CompleteOutput(bool success)
        {
            if (success)
            {
                writer.Complete(null);
            }
            else
            {
                writer.Complete(_outputCompleteException);
            }
        }

        /// <summary>Copies the contents of a <see cref="PipeReader"/> into this <see cref="PipeWriter" />.</summary>
        /// <param name="reader">The pipe reader to copy. This method does not complete it.</param>
        /// <param name="writesClosed">A task that completes when the writer can no longer write.</param>
        /// <param name="endStream">When <see langword="true" />, no more data will be written to the writer
        /// after the contents of the pipe reader.</param>
        /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The flush result. <see cref="FlushResult.IsCanceled" /> is <langword name="true"/> when the
        /// copying is interrupted by a call to <see cref="PipeReader.CancelPendingRead" /> on
        /// <paramref name="reader" />.</returns>
        internal async ValueTask<FlushResult> CopyFromAsync(
            PipeReader reader,
            Task writesClosed,
            bool endStream,
            CancellationToken cancellationToken)
        {
            FlushResult flushResult;
            if (reader.TryRead(out ReadResult readResult))
            {
                // We optimize for the very common situation where the all the reader bytes are available.
                flushResult = await WriteReadResultAsync().ConfigureAwait(false);

                if (readResult.IsCompleted || flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    return flushResult;
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

                    // This FlushAsync either throws an exception because the writer failed, or returns a completed
                    // FlushResult.
                    return await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                // we let other exceptions thrown by ReadAsync (including possibly an OperationCanceledException
                // thrown incorrectly) escape.

                flushResult = await WriteReadResultAsync().ConfigureAwait(false);
            }
            while (!readResult.IsCompleted && !flushResult.IsCanceled && !flushResult.IsCompleted);

            return flushResult;

            async ValueTask<FlushResult> WriteReadResultAsync()
            {
                if (readResult.IsCanceled)
                {
                    // The application (or an interceptor/middleware) called CancelPendingRead on reader.
                    reader.AdvanceTo(readResult.Buffer.Start); // Did not consume any byte in reader.

                    // The copy was canceled.
                    return new FlushResult(isCanceled: true, isCompleted: true);
                }
                else
                {
                    try
                    {
                        return await writer.WriteAsync(
                            readResult.Buffer,
                            readResult.IsCompleted && endStream,
                            cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        reader.AdvanceTo(readResult.Buffer.End);
                    }
                }
            }
        }

        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="source">The source sequence.</param>
        /// <param name="endStream">When <see langword="true" />, no more data will be written to the writer.</param>
        /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The flush result.</returns>
        private async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancellationToken)
        {
            if (writer is ReadOnlySequencePipeWriter readOnlySequenceWriter)
            {
                return await readOnlySequenceWriter.WriteAsync(source, endStream, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                FlushResult flushResult = default;
                if (source.IsEmpty)
                {
                    flushResult = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (source.IsSingleSegment)
                {
                    flushResult = await writer.WriteAsync(source.First, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    foreach (ReadOnlyMemory<byte> buffer in source)
                    {
                        flushResult = await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (flushResult.IsCompleted || flushResult.IsCanceled)
                        {
                            break;
                        }
                    }
                }
                return flushResult;
            }
        }
    }
}
