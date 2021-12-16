// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal static class PipeWriterExtensions
    {
        /// <summary>Copies source to a sink pipe writer. Also optionally completes this sink upon successful
        /// completion, i.e. when the source is fully read.
        /// </summary>
        /// <param name="sink">The sink pipe writer.</param>
        /// <param name="source">The source pipe reader.</param>
        /// <param name="completeWhenDone">When true, this method completes the writer after a successful copy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result from the last WriteAsync or the FlushAsync if there was no WriteAsync.</returns>
        internal static async Task<FlushResult> CopyFromAsync(
            this PipeWriter sink,
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            FlushResult flushResult;
            ReadResult readResult;

            var asyncCompleteSink = sink as AsyncCompletePipeWriter; // used by local functions below

            while (true)
            {
                try
                {
                    readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // TODO: is this exception translation correct?
                    throw new OperationCanceledException();
                }

                if (readResult.Buffer.IsEmpty)
                {
                    Debug.Assert(readResult.IsCanceled || readResult.IsCompleted);

                    flushResult = await FlushAsync(
                        complete: completeWhenDone && readResult.IsCompleted,
                        cancel).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        flushResult = await WriteAsync(
                            readResult.Buffer,
                            complete: completeWhenDone && readResult.IsCompleted,
                            cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        source.AdvanceTo(readResult.Buffer.End); // always fully consumed
                    }
                }

                if (readResult.IsCompleted || readResult.IsCanceled ||
                    flushResult.IsCompleted || flushResult.IsCanceled)
                {
                    break;
                }
            }

            if (completeWhenDone &&
                readResult.IsCompleted &&
                !flushResult.IsCompleted && !flushResult.IsCanceled &&
                asyncCompleteSink == null)
            {
                // Need to complete it now
                await sink.CompleteAsync().ConfigureAwait(false);
            }

            return flushResult;

            // Helper local function that takes the "optimized path" when asyncCompleteSink is not null.
            ValueTask<FlushResult> FlushAsync(
                bool complete,
                CancellationToken cancel) =>
                asyncCompleteSink?.WriteAsync(ReadOnlySequence<byte>.Empty, complete, cancel) ??
                    sink.FlushAsync(cancel);

            // Helper local function that takes the "optimized path" when asyncCompleteSink is not null.
            ValueTask<FlushResult> WriteAsync(
                ReadOnlySequence<byte> source,
                bool complete,
                CancellationToken cancel) =>
                asyncCompleteSink?.WriteAsync(source, complete, cancel) ?? sink.WriteAsync(source, cancel);
        }

        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="writer">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async ValueTask<FlushResult> WriteAsync(
            this PipeWriter writer,
            ReadOnlySequence<byte> source,
            CancellationToken cancel)
        {
            FlushResult result = default;

            if (source.IsSingleSegment)
            {
                result = await writer.WriteAsync(source.First, cancel).ConfigureAwait(false);
            }
            else
            {
                SequencePosition position = source.Start;

                // This executes at least twice.
                while (source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                {
                    result = await writer.WriteAsync(memory, cancel).ConfigureAwait(false);

                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break; // while
                    }
                }
            }
            return result;
        }
    }
}
