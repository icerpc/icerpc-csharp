// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests
{
    public static class PipeWriterExtensions
    {
        /// <summary>Writes a read only sequence of bytes to this writer.</summary>
        /// <param name="pipeWriter">The pipe writer.</param>
        /// <param name="source">The source sequence.</param>
        /// <param name="endStream">When true, no more data will be written to the sink.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The flush result.</returns>
        public static ValueTask<FlushResult> WriteAsync(
            this PipeWriter pipeWriter,
            ReadOnlyMemory<byte> source,
            bool endStream,
            CancellationToken cancel) =>
            pipeWriter.WriteAsync(new ReadOnlySequence<byte>(source), endStream, cancel);
    }
}
