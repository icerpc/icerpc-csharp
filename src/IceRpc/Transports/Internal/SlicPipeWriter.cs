// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeWriter : BufferedPipeWriter
    {
        private readonly SlicMultiplexedStream _stream;

        public override void Complete(Exception? exception = null)
        {
            base.Complete(exception);

            // If writes aren't marked as completed yet, abort stream writes. This will send a stream reset frame to
            // the peer to notify it won't receive additional data.
            if (!_stream.WritesCompleted)
            {
                if (exception == null)
                {
                    _stream.AbortWrite(SlicStreamError.NoError.ToError());
                }
                else if (exception is MultiplexedStreamAbortedException abortedException)
                {
                    _stream.AbortWrite(abortedException.ToError());
                }
                else
                {
                    _stream.AbortWrite(SlicStreamError.UnexpectedError.ToError());
                }
            }
        }

        internal SlicPipeWriter(SlicMultiplexedStream stream, MemoryPool<byte> pool, int minimumSegmentSize)
            : base(pool, minimumSegmentSize) => _stream = stream;

        private protected override ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> protocolHeader,
            ReadOnlySequence<byte> payload,
            bool completeWhenDone,
            CancellationToken cancel) =>
                _stream.WriteAsync(protocolHeader, payload, completeWhenDone, cancel);
    }
}
