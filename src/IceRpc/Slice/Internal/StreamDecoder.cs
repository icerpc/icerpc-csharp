// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IceRpc.Slice.Internal
{
    /// <summary>A stream decoder decodes a series of buffers into an IAsyncEnumerable{T}.</summary>
    /// <remarks>A stream decoder must be used by a single reader (that calls ReadAsync) and a single writer. The writer
    /// calls WriteAsync one or more times, and CompleteWriter when done. A stream decoder provides flow-control
    /// similar to the flow-control provided by the Pipelines library: the writer pauses after the number of bytes
    /// decoded but not read reaches the pause writer threshold, and the writer resumes when the number of bytes decoded
    /// but not read falls below the resume writer threshold. These bytes are not counted one by one but
    /// buffer-by-buffer: as soon as 1 element decoded from a buffer is read, all the bytes from that buffer are
    /// considered read.</remarks>
    internal class StreamDecoder<T>
    {
        private long _currentByteCount;
        private readonly Func<ReadOnlySequence<byte>, IEnumerable<T>> _decodeBufferFunc;
        private readonly Queue<(IEnumerable<T> Items, long ByteCount)> _queue = new();
        private readonly object _mutex = new();

        private readonly long _pauseWriterThreshold;

        private readonly SemaphoreSlim _readerSemaphore = new(initialCount: 0, maxCount: 1);

        private ReaderState _readerState = ReaderState.Running;

        private bool _readerStarted;

        private readonly long _resumeWriterThreshold;

        private readonly SemaphoreSlim _writerSemaphore = new(initialCount: 0, maxCount: 1);

        private WriterState _writerState = WriterState.Running;

        /// <summary>Constructs a stream decoder.</summary>
        /// <param name="decodeBufferFunc">The function that decodes a buffer into an enumerable of T.</param>
        /// <param name="options">The options to configure flow control.</param>
        internal StreamDecoder(
            Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc,
            SliceStreamDecoderOptions options)
        {
            _decodeBufferFunc = decodeBufferFunc;
            _pauseWriterThreshold = options.PauseWriterThreshold;
            _resumeWriterThreshold = options.ResumeWriterThreshold;
        }

        /// <summary>Constructs a stream decoder with the default options.</summary>
        /// <param name="decodeBufferFunc">The function that decodes a buffer into an enumerable of T.</param>
        internal StreamDecoder(Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc)
            : this(decodeBufferFunc, SliceStreamDecoderOptions.Default)
        {
        }

        /// <summary>Marks the writer as completed. This tells the reader no additional element will be decoded.
        /// </summary>
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
                        _readerSemaphore.Release();
                    }
                }
            }
        }

        /// <summary>Reads the stream decoder asynchronously.</summary>
        /// <param name="cancelCallback">An action to register with the cancellation token.</param>
        /// <param name="cancel">The cancellation token. It's typically set by calling the
        /// <see cref="TaskAsyncEnumerableExtensions.WithCancellation{T}"/> extension method on the returned async
        /// enumerable.</param>
        /// <returns>An async enumerable of T.</returns>
        /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
        /// <remarks>If the reader does not want to read the full async enumerable, it should cancel the cancellation
        /// token when done to notify the writer and avoid unnecessary writing/decoding.</remarks>
        internal async IAsyncEnumerable<T> ReadAsync(
            Action? cancelCallback = null,
            [EnumeratorCancellation] CancellationToken cancel = default)
        {
            using CancellationTokenRegistration _ = cancel.Register(() =>
            {
                CompleteReader();
                cancelCallback?.Invoke();
            });

            _readerStarted = _readerStarted == false ? true :
                throw new InvalidOperationException("a stream decoder can be read only once");

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
                    await _readerSemaphore.WaitAsync(cancel).ConfigureAwait(false);
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
                        _writerSemaphore.Release(); // we release 1 when we transition out of Paused
                    }
                }

                foreach (T item in items)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        yield break;
                    }
                    yield return item;
                }
            }
        }

        /// <summary>Writes a buffer to the stream decoder.</summary>
        /// <param name="buffer">The buffer. Cannot be empty.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task with a bool value that indicates whether or not the reader is completed. When the
        /// reader is completed, the writer should stop writing more and call <see cref="CompleteWriter"/>.</returns>
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
                await _writerSemaphore.WaitAsync(cancel).ConfigureAwait(false);
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
                    _readerSemaphore.Release(); // we release 1 when we transition out of Paused.
                }

                return false;
            }
        }

        /// <summary>Completes the reader. This method is called by ReadAsync when its cancellation token is cancelled.
        /// </summary>
        private void CompleteReader()
        {
            lock (_mutex)
            {
                if (_readerState != ReaderState.Completed)
                {
                    if (_readerState == ReaderState.Paused)
                    {
                        _readerSemaphore.Release();
                    }

                    _readerState = ReaderState.Completed;

                    // If the writer is paused, resume it so that it can return true ASAP.
                    if (_writerState == WriterState.Paused)
                    {
                        _writerState = WriterState.Running; // Only CompleteWriter marks writer as Completed
                        _writerSemaphore.Release();
                    }
                }
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
