// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>A PipeWriter that does nothing and always throws NotSupportedException except for
    /// Complete/CompleteAsync.</summary>
    internal class InvalidPipeWriter : PipeWriter
    {
        public override bool CanGetUnflushedBytes => false;
        public override long UnflushedBytes => throw _notSupportedException;

        /// <summary>A shared instance of this pipe writer.</summary>
        internal static PipeWriter Instance { get; } = new InvalidPipeWriter();

        private static readonly Exception _notSupportedException =
            new NotSupportedException("cannot use invalid pipe writer");

        public override void Advance(int bytes) => throw _notSupportedException;
        public override Stream AsStream(bool leaveOpen = false) => throw _notSupportedException;
        public override void CancelPendingFlush() => throw _notSupportedException;
        public override void Complete(Exception? exception)
        {
            // no-op
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
           throw _notSupportedException;

        public override Memory<byte> GetMemory(int sizeHint) => throw _notSupportedException;
        public override Span<byte> GetSpan(int sizeHint) => throw _notSupportedException;

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken) => throw _notSupportedException;

        protected override Task CopyFromAsync(Stream source, CancellationToken cancellationToken) =>
            throw _notSupportedException;
    }
}
