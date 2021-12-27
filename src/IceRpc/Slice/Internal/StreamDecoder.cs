// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IceRpc.Slice.Internal
{
    internal class StreamDecoder<T>
    {
        private long _currentByteCount;
        private readonly Func<ReadOnlySequence<byte>, IEnumerable<T>> _decodeBufferFunc;
        private readonly Queue<(IEnumerable<T> Items, long ByteCount)> _queue = new();
        private readonly object _mutex = new();

        private readonly long _pauseWriterThreshold;

        private readonly SemaphoreSlim _resumeReaderSemaphore = new(initialCount: 0, maxCount: 1);
        private readonly SemaphoreSlim _resumeWriterSemaphore = new(initialCount: 0, maxCount: 1);

        private ReaderState _readerState = ReaderState.Running;

        private readonly long _resumeWriterThreshold;

        private WriterState _writerState = WriterState.Running;

        internal StreamDecoder(
            Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc,
            StreamDecoderOptions options)
        {
            _decodeBufferFunc = decodeBufferFunc;
            _pauseWriterThreshold = options.PauseWriterThreshold;
            _resumeWriterThreshold = options.ResumeWriterThreshold;
        }

        internal void CompleteReader()
        {
            lock (_mutex)
            {
                if (_readerState != ReaderState.Completed)
                {
                    if (_readerState == ReaderState.Paused)
                    {
                        _resumeReaderSemaphore.Release();
                    }

                    _readerState = ReaderState.Completed;

                    // If the writer is paused, resume it so that it can return true ASAP.
                    if (_writerState == WriterState.Paused)
                    {
                        _writerState = WriterState.Running; // Only CompleteWriter marks writer as Completed
                        _resumeWriterSemaphore.Release();
                    }
                }
            }
        }

        internal void CompleteWriter()
        {
            lock (_mutex)
            {
                if (_writerState != WriterState.Completed)
                {
                    // CompleteWriter and WriteAsync should not be called concurrently.
                    Debug.Assert(_writerState == WriterState.Running);

                    _writerState = WriterState.Completed;

                    // If the reader is paused, mark it completed and release the semaphore.
                    if (_readerState == ReaderState.Paused)
                    {
                        _readerState = ReaderState.Completed;
                        _resumeReaderSemaphore.Release();
                    }
                }
            }
        }

        internal async IAsyncEnumerable<T> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
        {
            _ = cancel.Register(() => CompleteReader());

            while (true)
            {
                bool paused = false;

                lock (_mutex)
                {
                    if (_readerState == ReaderState.Completed)
                    {
                        yield break;
                    }

                    Debug.Assert(_readerState == ReaderState.Running);

                    if (_queue.Count == 0)
                    {
                        Debug.Assert(_currentByteCount == 0);

                        // Unless _queue.Count is 0, we don't care if _writerState is completed when reading: we need
                        // to read everything in the queue.

                        if (_writerState == WriterState.Completed)
                        {
                            // We will never get more items
                            _readerState = ReaderState.Completed;
                            yield break;
                        }

                        _readerState = ReaderState.Paused;
                        paused = true; // we wait for 1 when we transition to Paused
                    }
                }

                if (paused)
                {
                    await _resumeReaderSemaphore.WaitAsync(cancel).ConfigureAwait(false);
                }

                IEnumerable<T> items;

                lock (_mutex)
                {
                    if (_readerState == ReaderState.Completed)
                    {
                        yield break;
                    }
                    Debug.Assert(_readerState == ReaderState.Running);

                    (items, long byteCount) = _queue.Dequeue();

                    _currentByteCount -= byteCount;

                    if (_writerState == WriterState.Paused && _currentByteCount <= _resumeWriterThreshold)
                    {
                        _writerState = WriterState.Running;
                        _resumeWriterSemaphore.Release(); // we release 1 when we transition out of Paused
                    }
                }

                foreach (T item in items)
                {
                    yield return item;
                }
            }
        }

        internal async ValueTask<bool> WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancel)
        {
            if (buffer.IsEmpty)
            {
                throw new ArgumentException("cannot write empty buffer", nameof(buffer));
            }

            bool paused = false;

            lock (_mutex)
            {
                if (_writerState == WriterState.Completed)
                {
                    throw new InvalidOperationException("cannot write to completed writer");
                }

                if (_readerState == ReaderState.Completed)
                {
                    return true;
                }

                Debug.Assert(_writerState == WriterState.Running);

                if (_pauseWriterThreshold != 0 && _currentByteCount >= _pauseWriterThreshold)
                {
                    _writerState = WriterState.Paused;
                    paused = true; // we wait 1 when we transition to Paused.
                }
            }

            if (paused)
            {
                await _resumeWriterSemaphore.WaitAsync(cancel).ConfigureAwait(false);
            }

            IEnumerable<T> items = _decodeBufferFunc(buffer);
            Debug.Assert(items.Any());

            lock (_mutex)
            {
                if (_writerState == WriterState.Completed)
                {
                    throw new InvalidOperationException("cannot write to completed writer");
                }

                if (_readerState == ReaderState.Completed)
                {
                    return true;
                }

                Debug.Assert(_writerState == WriterState.Running);

                _queue.Enqueue((items, buffer.Length));
                _currentByteCount += buffer.Length;

                if (_readerState == ReaderState.Paused)
                {
                    Debug.Assert(_currentByteCount == buffer.Length);
                    _readerState = ReaderState.Running;
                    _resumeReaderSemaphore.Release(); // we release 1 when we transition out of Paused.
                }

                return false;
            }
        }

        private enum ReaderState
        {
            Running,
            Paused,
            Completed
        }

        private enum WriterState
        {
            Running,
            Paused,
            Completed
        }
    }
}
