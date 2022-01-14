// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal static class PipeWriterExtensions
    {
        /// <summary>Copies source to a sink pipe writer. Also optionally completes this sink upon successful
        /// completion, i.e. when the source is fully read.</summary>
        /// <param name="sink">The sink pipe writer.</param>
        /// <param name="source">The source pipe reader.</param>
        /// <param name="completeWhenDone">When true, this method completes the writer after a successful copy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async Task<FlushResult> CopyFromAsync(
            this PipeWriter sink,
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            FlushResult flushResult;
            if (sink is IMultiplexedStreamPipeWriter writer)
            {
                while (true)
                {
                    ReadResult readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        flushResult = await writer.WriteAsync(
                            readResult.Buffer,
                            completeWhenDone && readResult.IsCompleted,
                            cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        source.AdvanceTo(readResult.Buffer.End); // always fully consumed
                    }

                    if (readResult.IsCompleted || flushResult.IsCompleted)
                    {
                        flushResult = new FlushResult(isCanceled: false, isCompleted: true);
                        break;
                    }
                    else if (readResult.IsCanceled || flushResult.IsCanceled)
                    {
                        flushResult = new FlushResult(isCanceled: true, isCompleted: false);
                        break;
                    }
                }
            }
            else if (sink is AsyncCompletePipeWriter asyncWriter)
            {
                while (true)
                {
                    ReadResult readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        flushResult = await asyncWriter.WriteAsync(
                            readResult.Buffer,
                            completeWhenDone && readResult.IsCompleted,
                            cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        source.AdvanceTo(readResult.Buffer.End); // always fully consumed
                    }

                    if (readResult.IsCompleted || flushResult.IsCompleted)
                    {
                        flushResult = new FlushResult(isCanceled: false, isCompleted: true);
                        break;
                    }
                    else if (readResult.IsCanceled || flushResult.IsCanceled)
                    {
                        flushResult = new FlushResult(isCanceled: true, isCompleted: false);
                        break;
                    }
                }
            }
            else
            {
                while (true)
                {
                    ReadResult readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        // If readResult.Buffer.Length is small, it might be better to call a single
                        // sink.WriteAsync(readResult.Buffer.ToArray()) instead of calling multiple times WriteAsync
                        // that will end up in multiple network calls?
                        foreach (ReadOnlyMemory<byte> memory in readResult.Buffer)
                        {
                            flushResult = await sink.WriteAsync(memory, cancel).ConfigureAwait(false);
                            if (flushResult.IsCompleted || flushResult.IsCanceled)
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        source.AdvanceTo(readResult.Buffer.End);
                    }
                }
            }

            if (completeWhenDone && !flushResult.IsCompleted && !flushResult.IsCanceled)
            {
                // Complete the sink.
                await sink.CompleteAsync().ConfigureAwait(false);
            }

            return flushResult;
        }

        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="pipeWriter">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="completeWhenDone">When true, this method completes the writer after a successful write.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static ValueTask<FlushResult> WriteAsync(
            this PipeWriter pipeWriter,
            ReadOnlyMemory<byte> source,
            bool completeWhenDone,
            CancellationToken cancel) =>
            WriteAsync(pipeWriter, new ReadOnlySequence<byte>(source), completeWhenDone, cancel);

        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="pipeWriter">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="completeWhenDone">When true, this method completes the writer after a successful write.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async ValueTask<FlushResult> WriteAsync(
            this PipeWriter pipeWriter,
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            if (pipeWriter is IMultiplexedStreamPipeWriter writer)
            {
                return await writer.WriteAsync(source, completeWhenDone, cancel).ConfigureAwait(false);
            }
            else
            {
                SequencePosition position = source.Start;
                while (source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                {
                    FlushResult result = await pipeWriter.WriteAsync(memory, cancel).ConfigureAwait(false);
                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break; // while
                    }
                }

                if (completeWhenDone)
                {
                    await pipeWriter.CompleteAsync().ConfigureAwait(false);
                }

                return new FlushResult(isCanceled: false, isCompleted: completeWhenDone);
            }
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
