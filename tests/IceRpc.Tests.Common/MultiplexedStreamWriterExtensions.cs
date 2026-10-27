// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Tests.Common;

/// <summary>Provides extension methods for <see cref="IMultiplexedStreamWriter" /> that make tests shorter.</summary>
public static class MultiplexedStreamWriterExtensions
{
    /// <summary>Sends the unflushed bytes, if any.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The write result.</returns>
    public static ValueTask<WriteResult> FlushAsync(
        this IMultiplexedStreamWriter writer,
        CancellationToken cancellationToken = default) =>
        writer.WriteAsync(ReadOnlySequence<byte>.Empty, endStream: false, cancellationToken);

    /// <summary>Sends the unflushed bytes followed by <paramref name="source" />, without ending the stream.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="source">The bytes to send.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The write result.</returns>
    public static ValueTask<WriteResult> WriteAsync(
        this IMultiplexedStreamWriter writer,
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken = default) =>
        writer.WriteAsync(new ReadOnlySequence<byte>(source), endStream: false, cancellationToken);
}
