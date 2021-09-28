// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports.Slic;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The synchronized Slic frame writer decorator synchronizes concurrent calls to write Slic
    /// frames. It also ensures that Slic streams which are not started are started when the first stream data
    /// frame is written.</summary>
    internal sealed class SynchronizedSlicFrameWriterDecorator : ISlicFrameWriter
    {
        private readonly ISlicFrameWriter _decoratee;
        private long _nextBidirectionalId;
        private long _nextUnidirectionalId;
        private readonly AsyncSemaphore _sendSemaphore = new(1);

        public void Dispose()
        {
            _decoratee.Dispose();
            _sendSemaphore.Complete(new ConnectionClosedException());
        }

        public async ValueTask WriteFrameAsync(FrameType type, Action<IceEncoder> encode, CancellationToken cancel)
        {
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                await _decoratee.WriteFrameAsync(type, encode, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            FrameType type,
            Action<IceEncoder> encode,
            CancellationToken cancel)
        {
            if (!stream.IsStarted)
            {
                throw new NotSupportedException("can't send stream frame on non-started stream");
            }

            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                await _decoratee.WriteStreamFrameAsync(stream, type, encode, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                // If the stream is aborted, don't send additional stream frames. We make an exception for
                // unidirectional remote streams which need to send the StreamLast frame to ensure the local
                // stream releases the unidirectional semaphore.
                if (stream.WritesCompleted && (!stream.IsRemote || stream.IsBidirectional))
                {
                    throw new StreamAbortedException(StreamError.StreamAborted);
                }

                // Allocate stream ID if the stream isn't started. Thread-safety is provided by the send
                // semaphore.
                if (!stream.IsStarted)
                {
                    if (stream.IsBidirectional)
                    {
                        stream.Id = _nextBidirectionalId;
                        _nextBidirectionalId += 4;
                    }
                    else
                    {
                        stream.Id = _nextUnidirectionalId;
                        _nextUnidirectionalId += 4;
                    }
                }

                if (endStream)
                {
                    // At this point writes are considered completed on the stream. It's important to call
                    // this before sending the last packet to avoid a race condition where the peer could
                    // start a new stream before the Slic connection stream count is decreased.
                    stream.TrySetWriteCompleted();
                }

                await _decoratee.WriteStreamFrameAsync(stream, buffers, endStream, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        internal SynchronizedSlicFrameWriterDecorator(ISlicFrameWriter decoratee, bool isServer)
        {
            _decoratee = decoratee;

            // We use the same stream ID numbering scheme as Quic
            if (isServer)
            {
                _nextBidirectionalId = 1;
                _nextUnidirectionalId = 3;
            }
            else
            {
                _nextBidirectionalId = 0;
                _nextUnidirectionalId = 2;
            }
        }
    }
}
