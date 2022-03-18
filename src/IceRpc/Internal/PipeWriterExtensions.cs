// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal static class PipeWriterExtensions
    {
        /// <summary>Copies source to a sink pipe writer.</summary>
        /// <param name="sink">The sink pipe writer.</param>
        /// <param name="source">The source pipe reader.</param>
        /// <param name="endStream">When true, no more data will be written to the sink.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async Task<FlushResult> CopyFromAsync(
            this PipeWriter sink,
            PipeReader source,
            bool endStream,
            CancellationToken cancel)
        {
            FlushResult flushResult;

            if (sink is ReadOnlySequencePipeWriter writer)
            {
                while (true)
                {
                    ReadResult readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        flushResult = await writer.WriteAsync(
                            readResult.Buffer,
                            endStream && readResult.IsCompleted,
                            cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        source.AdvanceTo(readResult.Buffer.End); // always fully consumed
                    }

                    // TODO: can the sink or source actually be canceled?
                    if (readResult.IsCompleted || flushResult.IsCompleted ||
                        readResult.IsCanceled || flushResult.IsCanceled)
                    {
                        break;
                    }
                }
            }
            else
            {
                ReadResult readResult;
                while (true)
                {
                    readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                    flushResult = default;
                    try
                    {
                        // TODO: If readResult.Buffer.Length is small, it might be better to call a single
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

                    // TODO: can the sink or source actually be canceled?
                    if (readResult.IsCompleted || readResult.IsCanceled ||
                        flushResult.IsCompleted || flushResult.IsCanceled)
                    {
                        break;
                    }
                }

                if (!flushResult.IsCompleted && !flushResult.IsCanceled)
                {
                    flushResult = await sink.FlushAsync(cancel).ConfigureAwait(false);
                }
            }

            return flushResult;
        }

        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="pipeWriter">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="endStream">When true, no more data will be written to the sink.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        internal static async ValueTask<FlushResult> WriteAsync(
            this PipeWriter pipeWriter,
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancel)
        {
            if (pipeWriter is ReadOnlySequencePipeWriter writer)
            {
                return await writer.WriteAsync(source, endStream, cancel).ConfigureAwait(false);
            }
            else
            {
                // TODO: If readResult.Buffer.Length is small, it might be better to call a single
                // sink.WriteAsync(readResult.Buffer.ToArray()) instead of calling multiple times WriteAsync
                // that will end up in multiple network calls?
                FlushResult result = default;
                foreach (ReadOnlyMemory<byte> buffer in source)
                {
                    result = await pipeWriter.WriteAsync(buffer, cancel).ConfigureAwait(false);
                }
                return result;
            }
        }
    }
}
