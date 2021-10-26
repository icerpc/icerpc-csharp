// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc.Transports.Internal
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
        private readonly SlicMultiplexedStreamFactory _streamFactory;

        public void Dispose()
        {
            _decoratee.Dispose();
            _sendSemaphore.Complete(new ConnectionClosedException());
        }

        public async ValueTask WriteFrameAsync(
            SlicMultiplexedStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                await _decoratee.WriteFrameAsync(stream, buffers, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicMultiplexedStream stream,
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
                        _streamFactory.AddStream(_nextBidirectionalId, stream);
                        _nextBidirectionalId += 4;
                    }
                    else
                    {
                        _streamFactory.AddStream(_nextUnidirectionalId, stream);
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

        internal SynchronizedSlicFrameWriterDecorator(ISlicFrameWriter decoratee, SlicMultiplexedStreamFactory factory)
        {
            _decoratee = decoratee;
            _streamFactory = factory;

            // We use the same stream ID numbering scheme as Quic
            if (factory.IsServer)
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
