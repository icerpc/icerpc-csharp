// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>Represents a <see cref="PipeWriter" /> optimized for writing a <see cref="ReadOnlySequence{T}" />.
/// </summary>
/// <remarks>The <see cref="IDuplexPipe.Output" /> of <see cref="IMultiplexedStream" /> implementations must be a class
/// derived from <see cerf="ReadOnlySequencePipeWriter" />.</remarks>
public abstract class ReadOnlySequencePipeWriter : PipeWriter
{
    /// <summary>Writes a <see cref="ReadOnlySequence{T}" />.</summary>
    /// <param name="source">The source sequence.</param>
    /// <param name="endStream">If <see langword="true" />, no more data will be written to this pipe.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The flush result.</returns>
    public abstract ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken);
}
