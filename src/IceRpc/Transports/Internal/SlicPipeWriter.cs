// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeWriter : PipeWriter, IMultiplexedStreamPipeWriter
    {
        private bool _isWriterCompleted;
        private readonly SlicNetworkConnection _connection;
        private SequenceBufferWriter? _buffer;
        private readonly ReadOnlyMemory<byte> _sendHeader;
        private ReadOnlyMemory<byte>[] _sendBuffers = new ReadOnlyMemory<byte>[16];
        // The send credit left for sending data when flow control is enabled. When this reaches 0, no more
        // data can be sent to the peer until the _sendSemaphore is released. The _sendSemaphore will be
        // released when a StreamConsumed frame is received (indicating that the peer has additional space
        // for receiving data).
        private volatile int _sendCredit = int.MaxValue;
        // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private readonly SlicMultiplexedStream _stream;

        public override void Advance(int bytes)
        {
            CheckIfCompleted();
            if (_buffer == null)
            {
                throw new InvalidOperationException(
                    $"can't call ${nameof(Advance)} before {nameof(GetMemory)} or {nameof(GetSpan)}");
            }
            _buffer.Advance(bytes);
        }

        public override void CancelPendingFlush()
        {
            // TODO: XXX
        }

        public override void Complete(Exception? exception = null)
        {
            if (!_isWriterCompleted)
            {
                // If writes aren't marked as completed yet, abort stream writes. This will send a stream reset frame to
                // the peer to notify it won't receive additional data.
                if (!_stream.WritesCompleted)
                {
                    if (exception is null)
                    {
                        // Send empty stream frame to terminate the stream gracefully.
                        _ = WriteAsync(ReadOnlySequence<byte>.Empty, true, CancellationToken.None).AsTask();
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

                // Mark the writer as completed after calling WriteAsync, it would throw otherwise.
                _isWriterCompleted = true;

                _buffer?.Dispose();
            }
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancel)
        {
            CheckIfCompleted();

            if (_buffer == null)
            {
                return new FlushResult(isCanceled: false, isCompleted: false);
            }
            else
            {
                // WriteAsync will flush the internal buffer
                return await WriteAsync(
                    ReadOnlySequence<byte>.Empty,
                    completeWhenDone: false,
                    cancel).ConfigureAwait(false);
            }
        }

        public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel) =>
            // Writing an empty buffer completes the stream.
            WriteAsync(new ReadOnlySequence<byte>(source), completeWhenDone: source.Length == 0, cancel);

        public async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool completeWhenDone,
            CancellationToken cancel)
        {
            CheckIfCompleted();

            // Send the internal buffer first if one is set.
            bool sendingInternalBuffer = _buffer != null;
            ReadOnlySequence<byte> sendSource;
            if (_buffer == null)
            {
                sendSource = source;
            }
            else
            {
                sendSource = _buffer.WrittenSequence;
            }

            // Adopt the buffer to prevent it from being disposed before the write completes. The stream can complete
            // this writer before the last stream frame send completes (see
            // SynchronizedSlicFrameWriterDecorator.WriteStreamFrameAsync).
            using SequenceBufferWriter? buffer = _buffer;
            _buffer = null;

            while (true)
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

                // Gather the next buffers into _sendBuffers to send the stream frame. We get up to _sendCredit bytes
                // from the internal buffer or given source. If there are more bytes to send they will be sent into a
                // separate packet once the peer sends the StreamConsumed frame to provide additional send credit.
                int sendSize = 0;
                _sendBuffers[0] = _sendHeader;
                int sendBufferIndex = 1;
                while (sendSize < sendMaxSize)
                {
                    if (sendSource.IsEmpty && sendingInternalBuffer)
                    {
                        // Switch to sending the given source if the internal buffer is fully sent.
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
                    // If flow control is enabled, decrease the size of remaining data that we are allowed to send.
                    // If all the credit for sending data is consumed, _sendMaxSize will be 0 and we don't release
                    // the semaphore to prevent further sends. The semaphore will be released once the stream
                    // receives a StreamConsumed frame. It's important to decrease _sendCredit before sending the
                    // frame to avoid race conditions where the consumed frame could be received before we decreased
                    // it.
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
                catch (Exception ex)
                {
                    _sendSemaphore.Complete(ex);
                    throw;
                }

                if (!sendingInternalBuffer && sendSource.IsEmpty)
                {
                    Debug.Assert(!completeWhenDone || _stream.WritesCompleted);
                    break;
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: false);
        }

        public override Memory<byte> GetMemory(int sizeHint) =>
            (_buffer ??= new(_connection.Pool, _connection.MinimumSegmentSize)).GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint) =>
            (_buffer ??= new(_connection.Pool, _connection.MinimumSegmentSize)).GetSpan(sizeHint);

        internal SlicPipeWriter(SlicMultiplexedStream stream, SlicNetworkConnection connection)
        {
            _stream = stream;
            _connection = connection;

            // The first send buffer is always reserved for the Slic frame header.
            _sendHeader = SlicDefinitions.FrameHeader.ToArray();
            _sendBuffers[0] = _sendHeader;
            _sendCredit = _connection.PeerPauseWriterThreeshold;
        }

        internal void ReceivedConsumed(int size)
        {
            if (_sendSemaphore == null)
            {
                throw new InvalidDataException("invalid stream consumed frame, flow control is not enabled");
            }

            int newValue = Interlocked.Add(ref _sendCredit, size);
            if (newValue == size)
            {
                Debug.Assert(_sendSemaphore.Count == 0);
                _sendSemaphore.Release();
            }
            else if (newValue > _connection.PauseWriterThreeshold)
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
                throw new InvalidOperationException("writting is not allowed once the writer is completed");
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
