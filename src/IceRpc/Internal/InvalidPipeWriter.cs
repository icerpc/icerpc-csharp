// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A PipeWriter that does nothing and always throws InvalidOperationException except for Complete.</summary>
internal sealed class InvalidPipeWriter : PipeWriter
{
    public override bool CanGetUnflushedBytes => false;

    public override long UnflushedBytes => throw _invalidOperationException;

    /// <summary>Gets the InvalidPipeWriter singleton instance.</summary>
    internal static PipeWriter Instance { get; } = new InvalidPipeWriter();

    private static readonly Exception _invalidOperationException =
        new InvalidOperationException("Writing an invalid pipe writer is not allowed.");

    public override void Advance(int bytes) => throw _invalidOperationException;

    public override Stream AsStream(bool leaveOpen = false) => throw _invalidOperationException;

    public override void CancelPendingFlush() => throw _invalidOperationException;

    public override void Complete(Exception? exception)
    {
        // no-op
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
       throw _invalidOperationException;

    public override Memory<byte> GetMemory(int sizeHint) => throw _invalidOperationException;

    public override Span<byte> GetSpan(int sizeHint) => throw _invalidOperationException;

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken) => throw _invalidOperationException;

    protected override Task CopyFromAsync(Stream source, CancellationToken cancellationToken) =>
        throw _invalidOperationException;

    private InvalidPipeWriter()
    {
        // ensures there is only one instance
    }
}
