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
            ReadResult readResult;

            do
            {
                readResult = await source.ReadAsync(cancel).ConfigureAwait(false);
                try
                {
                    flushResult = await sink.WriteAsync(readResult.Buffer, endStream, cancel).ConfigureAwait(false);
                }
                finally
                {
                    source.AdvanceTo(readResult.Buffer.End);
                }
            } while (!readResult.IsCompleted && !readResult.IsCanceled &&
                     !flushResult.IsCompleted && !flushResult.IsCanceled);

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
                FlushResult flushResult = default;
                if (source.IsEmpty)
                {
                    flushResult = await pipeWriter.FlushAsync(cancel).ConfigureAwait(false);
                }
                else if (source.IsSingleSegment)
                {
                    flushResult = await pipeWriter.WriteAsync(source.First, cancel).ConfigureAwait(false);
                }
                else
                {
                    foreach (ReadOnlyMemory<byte> buffer in source)
                    {
                        flushResult = await pipeWriter.WriteAsync(buffer, cancel).ConfigureAwait(false);
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
