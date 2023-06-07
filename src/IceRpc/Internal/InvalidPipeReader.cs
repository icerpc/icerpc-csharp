// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A PipeReader that does nothing and always throws InvalidOperationException except for Complete.</summary>
internal sealed class InvalidPipeReader : PipeReader
{
    /// <summary>Gets the invalid pipe reader singleton instance.</summary>
    public static PipeReader Instance { get; } = new InvalidPipeReader();

    private const string ExceptionMessage = "Reading an invalid pipe reader is not allowed.";

    public override bool TryRead(out ReadResult result) => throw new InvalidOperationException(ExceptionMessage);

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(ExceptionMessage);

    public override void AdvanceTo(SequencePosition consumed) => throw new InvalidOperationException(ExceptionMessage);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        throw new InvalidOperationException(ExceptionMessage);

    public override void CancelPendingRead() => throw new InvalidOperationException(ExceptionMessage);

    public override void Complete(Exception? exception = null)
    {
        // no-op
    }

    private InvalidPipeReader()
    {
        // ensures there is only one instance
    }
}
