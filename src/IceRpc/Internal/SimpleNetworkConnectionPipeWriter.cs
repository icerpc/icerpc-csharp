// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a simple network connection. This is a pipe writer for a single request
    /// or response.</summary>
    internal class SimpleNetworkConnectionPipeWriter : PipeWriter, IDisposable
    {
        private readonly ISimpleNetworkConnection _connection;
        public override bool CanGetUnflushedBytes => _pipe.Writer.CanGetUnflushedBytes;
        public override long UnflushedBytes => _pipe.Writer.UnflushedBytes;

        private readonly Pipe _pipe = new();

        public override void Advance(int bytes) => _pipe.Writer.Advance(bytes);

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception = null)
        {
        }

        public override ValueTask CompleteAsync(Exception? exception = null) => new();

        public void Dispose()
        {
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancel) =>
            WriteAsync(ReadOnlyMemory<byte>.Empty, cancel);

        public override Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel)
        {
            // We're flushing our own internal pipe here.
            _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

            // If there's data to read from the internal pipe, read the data and write it before the given data.
            if (_pipe.Reader.TryRead(out ReadResult result))
            {
                ReadOnlyMemory<byte>[] buffers;
                if (source.IsEmpty && result.Buffer.IsSingleSegment)
                {
                    buffers = new ReadOnlyMemory<byte>[] { result.Buffer.First };
                }
                else
                {
                    int count = 0;
                    buffers = new ReadOnlyMemory<byte>[16];
                    foreach (ReadOnlyMemory<byte> buffer in result.Buffer)
                    {
                        buffers[count++] = buffer;

                        if (count == buffers.Length)
                        {
                            // Expand the buffers array.
                            Memory<ReadOnlyMemory<byte>> tmp = buffers;
                            buffers = new ReadOnlyMemory<byte>[buffers.Length + 16];
                            tmp.CopyTo(buffers);
                        }
                    }
                    if (!source.IsEmpty)
                    {
                        buffers[count++] = source;
                    }
                }

                await _connection.WriteAsync(buffers, cancel).ConfigureAwait(false);

                // The unflushed bytes are all consumed no matter what.
                _pipe.Reader.AdvanceTo(result.Buffer.End);
            }
            else
            {
                await _connection.WriteAsync(new ReadOnlyMemory<byte>[] { source }, cancel).ConfigureAwait(false);
            }

            return new FlushResult(isCanceled: false, isCompleted: false);
        }

        internal SimpleNetworkConnectionPipeWriter(ISimpleNetworkConnection connection) =>
            _connection = connection;
    }
}
