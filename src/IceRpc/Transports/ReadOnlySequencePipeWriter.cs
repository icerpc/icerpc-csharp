// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>The <see cref="ReadOnlySequencePipeWriter" /> abstract class should be extended by the <see
/// cref="PipeWriter" /> returned from the <see cref="IMultiplexedStream" /> implementation of <see
/// cref="IDuplexPipe.Output" />. It provides a <see cref="PipeWriter.WriteAsync" /> method with a <see
/// cref="ReadOnlySequence{T}" /> source and a boolean to notify the <see cref="IMultiplexedStream" /> implementation
/// that no more data will be written. This class optimizes the writing of a <see cref="ReadOnlySequence{T}" /> source
/// for transports that support a gather write API.</summary>
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
