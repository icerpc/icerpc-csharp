// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeWriter : ReadOnlySequencePipeWriter
    {
        private bool _isWriterCompleted;
        private readonly SlicNetworkConnection _connection;
        private readonly Pipe _pipe;
        private readonly ReadOnlyMemory<byte> _sendHeader;
        // TODO: once we refactor the SimpleNetworkConnection API with a write method that takes a
        // ReadOnlySequence<byte>, we should instead cache a ReadOnlySequence<byte> used to compose the internal pipe
        // data with the ReadOnlySequence<byte> provided to the WriteAsync method.
        private ReadOnlyMemory<byte>[] _sendBuffers = new ReadOnlyMemory<byte>[16];
        // The send credit left for sending data when flow control is enabled. When this reaches 0, no more
        // data can be sent to the peer until the _sendSemaphore is released. The _sendSemaphore will be
        // released when a StreamResumeWrite frame is received (indicating that the peer has additional space
        // for receiving data).
        private volatile int _sendCredit = int.MaxValue;
        // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private readonly SlicMultiplexedStream _stream;

        public override void Advance(int bytes)
        {
            CheckIfCompleted();
            _pipe.Writer.Advance(bytes);
        }

        public override void CancelPendingFlush() => _pipe.Writer.CancelPendingFlush();

        public override void Complete(Exception? exception = null)
        {
            if (!_isWriterCompleted)
            {
                // If writes aren't marked as completed yet, abort stream writes. This will send a stream reset frame to
                // the peer to notify it won't receive additional data (unless the connection has been lost).
                if (!_stream.WritesCompleted)
                {
                    if (exception == null)
                    {
                        if (_pipe.Writer.UnflushedBytes > 0)
                        {
                            throw new InvalidOperationException(
                                "cannot call Complete on a SlicPipeWriter with unflushed bytes");
                        }
                        _stream.AbortWrite(SlicStreamError.NoError.ToError());
                    }
                    else if (exception is MultiplexedStreamAbortedException abortedException)
                    {
                        _stream.AbortWrite(abortedException.ToError());
                    }
                    else if (exception is not ConnectionLostException)
                    {
                        _stream.AbortWrite(SlicStreamError.UnexpectedError.ToError());
                    }
                }

                // Mark the writer as completed after calling WriteAsync, it would throw otherwise.
                _isWriterCompleted = true;

                // The Pipe reader/writer implementations don't block so it's safe to call the synchronous complete
                // methods here.
                _pipe.Writer.Complete();
                _pipe.Reader.Complete();
            }
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancel)
        {
            CheckIfCompleted();

            // WriteAsync will flush the internal buffer
            return await WriteAsync(
                ReadOnlySequence<byte>.Empty,
                completeWhenDone: false,
                cancel).ConfigureAwait(false);
        }

        public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel) =>
            // Writing an empty buffer completes the stream.
            WriteAsync(new ReadOnlySequence<byte>(source), completeWhenDone: source.Length == 0, cancel);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            CheckIfCompleted();

            // If there's internal pipe data, send it first. Otherwise, send the given source.
            ReadOnlySequence<byte> internalBuffer = ReadOnlySequence<byte>.Empty;
            ReadOnlySequence<byte> sendSource = ReadOnlySequence<byte>.Empty;
            bool sendingInternalBuffer = false;
            if (_pipe.Writer.UnflushedBytes > 0)
            {
                // The FlushAsync call on the pipe should never block since the pipe uses an inline writer scheduler
                // and PauseWriterThreshold is set to zero.
                ValueTask<FlushResult> flushResultTask = _pipe.Writer.FlushAsync(CancellationToken.None);
                Debug.Assert(flushResultTask.IsCompleted);
                _ = await flushResultTask.ConfigureAwait(false);

                ValueTask<ReadResult> readResultTask = _pipe.Reader.ReadAsync(CancellationToken.None);
                Debug.Assert(readResultTask.IsCompleted);
                ReadResult readResult = await readResultTask.ConfigureAwait(false);

                Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled);
                internalBuffer = readResult.Buffer;
                sendSource = internalBuffer;
                sendingInternalBuffer = true;
            }
            else
            {
                sendSource = source;
            }

            try
            {
                do
                {
                    // Check if writes completed, the stream might have been reset by the peer. Don't send the data and
                    // return a completed flush result.
                    if (_stream.WritesCompleted)
                    {
                        if (_stream.ResetError is long error &&
                            error.ToSlicError() is SlicStreamError slicError &&
                            slicError != SlicStreamError.NoError)
                        {
                            throw new MultiplexedStreamAbortedException(error);
                        }
                        else
                        {
                            return new FlushResult(isCanceled: false, isCompleted: true);
                        }
                    }

                    // Acquire the semaphore to ensure flow control allows sending additional data. It's important to
                    // acquire the semaphore before checking _sendCredit. The semaphore acquisition will block if we
                    // can't send additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are
                    // allowed to send additional data and _sendCredit can be used to figure out the size of the next
                    // packet to send.
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    Debug.Assert(_sendCredit > 0);
                    int sendMaxSize = Math.Min(_sendCredit, _connection.PeerPacketMaxSize);

                    // Gather the next buffers into _sendBuffers to send the stream frame. We get up to _sendCredit
                    // bytes from the internal buffer or given source. If there are more bytes to send they will be sent
                    // into a separate packet once the peer sends the StreamResumeWrite frame to provide additional send
                    // credit.
                    int sendSize = 0;
                    _sendBuffers[0] = _sendHeader;
                    int sendBufferIndex = 1;
                    while (sendSize < sendMaxSize)
                    {
                        if (sendingInternalBuffer && sendSource.IsEmpty)
                        {
                            // Switch to sending the given source since we've consumed all the data from the internal
                            // pipe.
                            sendingInternalBuffer = false;
                            sendSource = source;
                        }

                        if (sendSource.IsEmpty)
                        {
                            // No more data to send!
                            break;
                        }

                        // Add the send source data to the send buffers.
                        sendSize += FillSendBuffers(ref sendSource, ref sendBufferIndex, sendMaxSize - sendSize);
                    }

                    try
                    {
                        // Decrease the size of remaining data that we are allowed to send. If all the credit for
                        // sending data is consumed, _sendMaxSize will be 0 and we don't release the semaphore to
                        // prevent further sends. The semaphore will be released once the stream receives a
                        // StreamResumeWrite frame. It's important to decrease _sendCredit before sending the frame to
                        // avoid race conditions where the consumed frame could be received before we decreased it.
                        int sendCredit = Interlocked.Add(ref _sendCredit, -sendSize);
                        if (sendSize > 0 || completeWhenDone)
                        {
                            // Send the stream frame.
                            await _connection.SendStreamFrameAsync(
                                _stream,
                                _sendBuffers.AsMemory()[0..sendBufferIndex],
                                endStream: completeWhenDone && !sendingInternalBuffer && sendSource.IsEmpty,
                                cancel).ConfigureAwait(false);
                        }

                        // If flow control allows sending more data, release the semaphore.
                        if (sendCredit > 0)
                        {
                            _sendSemaphore.Release();
                        }
                    }
                    catch (MultiplexedStreamAbortedException ex)
                    {
                        if (ex.ToSlicError() == SlicStreamError.NoError)
                        {
                            return new FlushResult(isCanceled: false, isCompleted: true);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        _sendSemaphore.Complete(ex);
                        throw;
                    }
                }
                while (sendingInternalBuffer || !sendSource.IsEmpty);
            }
            finally
            {
                if (internalBuffer.Length > 0)
                {
                    _pipe.Reader.AdvanceTo(internalBuffer.End);

                    // Make sure there's no more data to consume from the pipe.
                    Debug.Assert(!_pipe.Reader.TryRead(out ReadResult _));
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: false);
        }

        public override Memory<byte> GetMemory(int sizeHint) => _pipe.Writer.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint) => _pipe.Writer.GetSpan(sizeHint);

        internal SlicPipeWriter(SlicMultiplexedStream stream, SlicNetworkConnection connection)
        {
            _stream = stream;
            _connection = connection;

            // Create a pipe that never pauses on flush or write. The SlicePipeWriter will pause the flush or write if
            // the Slic flow control doesn't permit sending more data. We also use an inline pipe scheduler for write to
            // avoid thread context switches when FlushAsync is called on the internal pipe writer.
            _pipe = new(new PipeOptions(
                pool: _connection.Pool,
                minimumSegmentSize: _connection.MinimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));

            // The first send buffer is always reserved for the Slic frame header.
            _sendHeader = SlicDefinitions.FrameHeader.ToArray();
            _sendBuffers[0] = _sendHeader;
            _sendCredit = _connection.PeerPauseWriterThreshold;
        }

        internal void ReceivedConsumed(int size)
        {
            int newValue = Interlocked.Add(ref _sendCredit, size);
            if (newValue == size)
            {
                Debug.Assert(_sendSemaphore.Count == 0);
                _sendSemaphore.Release();
            }
            else if (newValue > _connection.PauseWriterThreshold)
            {
                // The peer is trying to increase the credit to a value which is larger than what it is allowed to.
                throw new InvalidDataException("invalid flow control credit increase");
            }
        }

        private void CheckIfCompleted()
        {
            if (_isWriterCompleted)
            {
                // If the writer is completed, the caller is bogus, it shouldn't call writer operations after completing
                // the pipe writer.
                throw new InvalidOperationException("writing is not allowed once the writer is completed");
            }
        }

        /// <summary>Consumes up to maxSize data from the given source and fill the send buffers. The source is sliced
        /// upon return to match the data left to be consumed.</summary>
        private int FillSendBuffers(ref ReadOnlySequence<byte> source, ref int sendBufferIndex, int maxSize)
        {
            Debug.Assert(maxSize > 0);
            int size = 0;
            SequencePosition position = source.Start;
            while (true)
            {
                if (!source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                {
                    // No more data available.
                    source = ReadOnlySequence<byte>.Empty;
                    return size;
                }

                if (sendBufferIndex == _sendBuffers.Length)
                {
                    // Reallocate the send buffer array if it's too small.
                    ReadOnlyMemory<byte>[] tmp = _sendBuffers;
                    _sendBuffers = new ReadOnlyMemory<byte>[_sendBuffers.Length * 2];
                    tmp.CopyTo(_sendBuffers.AsMemory());
                }

                if (size + memory.Length < maxSize)
                {
                    // Copy the segment to the send buffers.
                    _sendBuffers[sendBufferIndex++] = memory;
                    size += memory.Length;
                }
                else
                {
                    // We've reached the maximum send size. Slice the buffer to send and slice the source buffer
                    // to the remaining data to consume.
                    _sendBuffers[sendBufferIndex++] = memory[0..(maxSize - size)];
                    size += maxSize - size;
                    source = source.Slice(source.GetPosition(size));
                    return size;
                }
            }
        }
    }
}
