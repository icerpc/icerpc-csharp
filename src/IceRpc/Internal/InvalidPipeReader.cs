// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A PipeReader that does nothing and always throws NotSupportedException except for Complete.</summary>
internal sealed class InvalidPipeReader : PipeReader
{
    /// <summary>Gets the invalid pipe reader singleton instance.</summary>
    public static PipeReader Instance { get; } = new InvalidPipeReader();

    private static readonly Exception _notSupportedException =
        new NotSupportedException("Cannot use invalid pipe reader.");

    public override bool TryRead(out ReadResult result) => throw _notSupportedException;

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        throw _notSupportedException;

    public override void AdvanceTo(SequencePosition consumed) => throw _notSupportedException;

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        throw _notSupportedException;

    public override void CancelPendingRead() => throw _notSupportedException;

    public override void Complete(Exception? exception = null)
    {
        // no-op
    }

    private InvalidPipeReader()
    {
        // ensures there is only one instance
    }
}
