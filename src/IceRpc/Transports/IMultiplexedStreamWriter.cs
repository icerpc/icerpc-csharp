// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>Writes bytes to the output of an <see cref="IMultiplexedStream" />. The <see cref="IBufferWriter{T}" />
/// methods buffer bytes locally; the next <see cref="WriteAsync" /> or <see cref="CompleteAsync" /> call sends them.
/// IceRPC uses them to encode the request and response headers in place, so that a header rides in the same frame as
/// the first payload bytes.</summary>
/// <remarks>The owner of this writer calls exactly one of <see cref="CompleteAsync" /> and <see cref="Abort" />.
/// An implementation makes both calls idempotent: a call after the writer is completed does nothing.</remarks>
public interface IMultiplexedStreamWriter : IBufferWriter<byte>
{
    /// <summary>Gets the number of bytes buffered by the <see cref="IBufferWriter{T}" /> methods and not yet sent.
    /// IceRPC reads this property before and after encoding a header to compute the size of this header, since parts
    /// of the header can be written directly with the <see cref="IBufferWriter{T}" /> methods.</summary>
    /// <value>The number of unflushed bytes.</value>
    long UnflushedBytes { get; }

    /// <summary>Sends the unflushed bytes followed by <paramref name="source" />.</summary>
    /// <param name="source">The bytes to send.</param>
    /// <param name="endStream">When <see langword="true" />, no more bytes will be written: the transport marks this
    /// frame as the last one of the stream, and later calls to <see cref="WriteAsync" /> and <see
    /// cref="IBufferWriter{T}.Advance" /> throw <see cref="InvalidOperationException" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns><see cref="WriteResult.Written" />, or <see cref="WriteResult.WritesClosed" /> when the peer has
    /// closed its reads and the bytes were discarded.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the writer is completed or a previous call to <see
    /// cref="WriteAsync" /> had <paramref name="endStream" /> set.</exception>
    ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken);

    /// <summary>Completes this writer gracefully: sends the unflushed bytes and any trailing bytes the implementation
    /// produces, closes the writes of the stream if a <see cref="WriteAsync" /> call with <c>endStream</c> hasn't
    /// already, and releases the resources of this writer. The peer having closed its reads is not a failure.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the writer is completed.</returns>
    /// <remarks>When this method fails, it aborts the writer before throwing: the caller does not need to call <see
    /// cref="Abort" /> afterwards.</remarks>
    ValueTask CompleteAsync(CancellationToken cancellationToken);

    /// <summary>Aborts this writer: discards the unflushed bytes, closes the writes of the stream abruptly and
    /// releases the resources of this writer. This method never blocks.</summary>
    void Abort();
}

/// <summary>The result of <see cref="IMultiplexedStreamWriter.WriteAsync" />.</summary>
public enum WriteResult
{
    /// <summary>The bytes were written.</summary>
    Written,

    /// <summary>The peer closed its reads: the bytes were discarded and the writes of the stream are now closed. This
    /// is the event that completes <see cref="IMultiplexedStream.WritesClosed" />.</summary>
    WritesClosed,
}
