// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A PipeWriter that does nothing and always throws InvalidOperationException except for Complete.</summary>
internal sealed class InvalidPipeWriter : PipeWriter
{
    public override bool CanGetUnflushedBytes => false;

    public override long UnflushedBytes => throw new InvalidOperationException(ExceptionMessage);

    /// <summary>Gets the InvalidPipeWriter singleton instance.</summary>
    internal static PipeWriter Instance { get; } = new InvalidPipeWriter();

    private const string ExceptionMessage = "Writing an invalid pipe writer is not allowed.";

    public override void Advance(int bytes) => throw new InvalidOperationException(ExceptionMessage);

    public override Stream AsStream(bool leaveOpen = false) => throw new InvalidOperationException(ExceptionMessage);

    public override void CancelPendingFlush() => throw new InvalidOperationException(ExceptionMessage);

    public override void Complete(Exception? exception)
    {
        // no-op
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
       throw new InvalidOperationException(ExceptionMessage);

    public override Memory<byte> GetMemory(int sizeHint) => throw new InvalidOperationException(ExceptionMessage);

    public override Span<byte> GetSpan(int sizeHint) => throw new InvalidOperationException(ExceptionMessage);

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken) => throw new InvalidOperationException(ExceptionMessage);

    protected override Task CopyFromAsync(Stream source, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(ExceptionMessage);

    private InvalidPipeWriter()
    {
        // ensures there is only one instance
    }
}
