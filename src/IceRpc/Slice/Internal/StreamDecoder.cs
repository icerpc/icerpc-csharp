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
        private readonly Queue<(IEnumerable<T> Items, long byteCount)> _queue = new();
        private readonly object _mutex = new();

        private readonly long _pauseWriterThreshold;

        private readonly SemaphoreSlim _resumeReaderSemaphore = new(initialCount: 0, maxCount: 1);
        private readonly SemaphoreSlim _resumeWriterSemaphore = new(initialCount: 0, maxCount: 1);

        private ReaderState _readerState = ReaderState.Running;

        private readonly long _resumeWriterThreshold;

        private WriterState _writerState = WriterState.Running;

        internal StreamDecoder(
            Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc,
            long pauseWriterThreshold,
            long resumeWriterThreshold)
        {
            if (resumeWriterThreshold > pauseWriterThreshold)
            {
                throw new ArgumentException(
                    $"{nameof(resumeWriterThreshold)} must be smaller than ${nameof(pauseWriterThreshold)}",
                    nameof(resumeWriterThreshold));
            }

            if (resumeWriterThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resumeWriterThreshold),
                    $"{nameof(resumeWriterThreshold)} must be greater than 0");
            }

            _decodeBufferFunc = decodeBufferFunc;
            _pauseWriterThreshold = pauseWriterThreshold;
            _resumeWriterThreshold = resumeWriterThreshold;
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
                    // If the writer is a paused, we don't release resumeWriterSemaphore because CompleteWriter and
                    // WriteAsync should not be called concurrently.

                    _writerState = WriterState.Completed;

                    // If the reader is paused, resume it so that it can complete ASAP.
                    if (_readerState == ReaderState.Paused)
                    {
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
                bool paused;

                lock (_mutex)
                {
                    if (_readerState == ReaderState.Completed)
                    {
                        yield break;
                    }

                    paused = _readerState == ReaderState.Paused;
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

                    if (paused)
                    {
                        Debug.Assert(_readerState == ReaderState.Paused);
                        _readerState = ReaderState.Running;
                    }

                    if (_queue.TryDequeue(out (IEnumerable<T> Items, long ByteCount) entry))
                    {
                        _currentByteCount -= entry.ByteCount;
                        items = entry.Items;

                        // If the writer is paused and we cross the resume writer threshold, resume the writer
                        if (_writerState == WriterState.Paused && _currentByteCount <= _resumeWriterThreshold)
                        {
                            _resumeWriterSemaphore.Release();
                        }
                    }
                    else
                    {
                        // If the writer is completed, we won't get additional items
                        if (_writerState == WriterState.Completed)
                        {
                            _readerState = ReaderState.Completed;
                            yield break;
                        }

                        // else switch to the paused state and continue while loop
                        _readerState = ReaderState.Paused;
                        continue;
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

            bool paused;

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

                paused = _writerState == WriterState.Paused;
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

                if (paused)
                {
                    Debug.Assert(_writerState == WriterState.Paused);
                    _writerState = WriterState.Running;
                }

                if (_readerState == ReaderState.Completed)
                {
                    return true;
                }

                _queue.Enqueue((items, buffer.Length));
                _currentByteCount += buffer.Length;

                if (_currentByteCount >= _pauseWriterThreshold)
                {
                    _writerState = WriterState.Paused;
                }

                // If the reader is paused, release semaphore
                if (_readerState == ReaderState.Paused)
                {
                    _resumeReaderSemaphore.Release();
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
