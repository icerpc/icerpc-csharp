// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a write-only stream over an PipeWriter.</summary>
    /// <remarks>This is not a general-purpose Stream implementation. This stream is only called indirectly by
    /// <see cref="Ice1ProtocolConnection"/> and <see cref="Ice2ProtocolConnection"/> when they copy the payload
    /// source of an outgoing frame to the payload sink of that frame.</remarks>
    internal class PipeWriterStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        private readonly PipeWriter _writer;

        public override IAsyncResult BeginWrite(
            byte[] buffer,
            int offset,
            int count,
            AsyncCallback? callback,
            object? state) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync()
        {
            await _writer.CompleteAsync().ConfigureAwait(false);

            // base.DisposeAsync calls Dispose
            await base.DisposeAsync().ConfigureAwait(false);
        }

        public override void EndWrite(IAsyncResult asyncResult) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushResult flushResult = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (flushResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => _writer.Write(buffer); // makes a copy

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken = default) =>
            WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            FlushResult flushResult = await _writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (flushResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }
        }

        internal PipeWriterStream(PipeWriter writer) => _writer = writer;
    }
}
