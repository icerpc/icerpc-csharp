// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A PipeReader that does nothing and always throws InvalidOperationException except for Complete.</summary>
internal sealed class InvalidPipeReader : PipeReader
{
    /// <summary>Gets the invalid pipe reader singleton instance.</summary>
    public static PipeReader Instance { get; } = new InvalidPipeReader();

    private static readonly Exception _invalidOperationException =
        new InvalidOperationException("Reading an invalid pipe reader is not allowed.");

    public override bool TryRead(out ReadResult result) => throw _invalidOperationException;

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        throw _invalidOperationException;

    public override void AdvanceTo(SequencePosition consumed) => throw _invalidOperationException;

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        throw _invalidOperationException;

    public override void CancelPendingRead() => throw _invalidOperationException;

    public override void Complete(Exception? exception = null)
    {
        // no-op
    }

    private InvalidPipeReader()
    {
        // ensures there is only one instance
    }
}
