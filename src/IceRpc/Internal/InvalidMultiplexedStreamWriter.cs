// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Internal;

/// <summary>A multiplexed stream writer that throws <see cref="InvalidOperationException" /> for every operation
/// except <see cref="Complete" />.</summary>
internal sealed class InvalidMultiplexedStreamWriter : IMultiplexedStreamWriter
{
    public long UnflushedBytes => throw new InvalidOperationException(ExceptionMessage);

    /// <summary>Gets the InvalidMultiplexedStreamWriter singleton instance.</summary>
    internal static IMultiplexedStreamWriter Instance { get; } = new InvalidMultiplexedStreamWriter();

    private const string ExceptionMessage = "Writing to an invalid multiplexed stream writer is not allowed.";

    public void Advance(int count) => throw new InvalidOperationException(ExceptionMessage);

    public void Complete(Exception exception)
    {
        // no-op
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException(ExceptionMessage);

    public Memory<byte> GetMemory(int sizeHint = 0) => throw new InvalidOperationException(ExceptionMessage);

    public Span<byte> GetSpan(int sizeHint = 0) => throw new InvalidOperationException(ExceptionMessage);

    public ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken) => throw new InvalidOperationException(ExceptionMessage);

    private InvalidMultiplexedStreamWriter()
    {
        // ensures there is only one instance
    }
}
