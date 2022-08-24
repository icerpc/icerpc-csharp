// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal static class PipeWriterExtensions
{
    /// <summary>Writes a read only sequence of bytes to this writer.</summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="source">The source sequence.</param>
    /// <param name="endStream">When true, no more data will be written to the writer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The flush result.</returns>
    internal static async ValueTask<FlushResult> WriteAsync(
        this PipeWriter writer,
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (writer is ReadOnlySequencePipeWriter readonlySequenceWriter)
        {
            return await readonlySequenceWriter.WriteAsync(source, endStream, cancellationToken).ConfigureAwait(false);
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
                // TODO: If readResult.Buffer.Length is small, it might be better to call a single
                // writer.WriteAsync(readResult.Buffer.ToArray()) instead of calling multiple times WriteAsync
                // that will end up in multiple transport calls?
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
