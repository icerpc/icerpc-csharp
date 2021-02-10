// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>A lightweight semaphore implementation that provides FIFO guarantee for EnterAsync. EnterAsync
    /// also relies on ManualResetValueTaskCompletionSource to minimize heap allocations and provide a ValueTask
    /// based EnterAsync operation.</summary>
    internal class AsyncSemaphore
    {
        internal int Count
        {
            get
            {
                lock (_mutex)
                {
                    return _currentCount;
                }
            }
        }

        private int _currentCount;
        private Exception? _exception;
        private readonly int _maxCount;
        private readonly object _mutex = new();
        private readonly Queue<ManualResetValueTaskCompletionSource<bool>> _queue = new();

        internal AsyncSemaphore(int initialCount)
        {
            _currentCount = initialCount;
            _maxCount = initialCount;
        }

        /// <summary>Notify callers that are waiting to enter the semaphore that the semaphore is being terminated.
        /// The given exception will be raised by the awaited EnterAsync operation.</summary>
        /// <param name="exception">The exception raised to notify the callers waiting to enter the semaphore of the
        /// completion.</param>
        internal void Complete(Exception? exception = null)
        {
            lock (_mutex)
            {
                if (_exception != null)
                {
                    return;
                }

                _exception = exception;

                // While we could instead use the EnterAsync cancellation token to cancel the operation, it's
                // simpler and more efficient to trigger the cancellation directly by setting the exception on
                // the task completion source. It also ensures the awaiters will throw the given exception
                // instead of a generic OperationCanceledException.
                foreach (ManualResetValueTaskCompletionSource<bool> source in _queue)
                {
                    try
                    {
                        if (exception != null)
                        {
                            source.SetException(exception);
                        }
                        else
                        {
                            source.SetResult(false);
                        }
                    }
                    catch
                    {
                        // Ignore, the source might already be completed if canceled.
                    }
                }
            }
        }

        /// <summary>Asynchronously enter the semaphore. If the semaphore can't be entered, this method waits
        /// until the semaphore is released.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <exception name="InvalidOperationException">Thrown when the semaphore is completed.</exception>
        internal async ValueTask EnterAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            ManualResetValueTaskCompletionSource<bool> taskCompletionSource;
            CancellationTokenRegistration? tokenRegistration = null;
            lock (_mutex)
            {
                if (_exception != null)
                {
                    throw _exception;
                }

                if (_currentCount > 0)
                {
                    Debug.Assert(_queue.Count == 0);
                    --_currentCount;
                    return;
                }

                // Don't auto reset the task completion source after obtaining the result. This is necessary to
                // ensure that the exception won't be cleared if the task is canceled.
                taskCompletionSource = new ManualResetValueTaskCompletionSource<bool>(autoReset: false);
                taskCompletionSource.RunContinuationAsynchronously = true;
                if (cancel.CanBeCanceled)
                {
                    cancel.ThrowIfCancellationRequested();
                    tokenRegistration = cancel.Register(
                        () =>
                        {
                            lock (_mutex)
                            {
                                if (_queue.Contains(taskCompletionSource))
                                {
                                    taskCompletionSource.SetException(new OperationCanceledException(cancel));
                                }
                            }
                        });
                }
                _queue.Enqueue(taskCompletionSource);
            }

            try
            {
                await taskCompletionSource.ValueTask.ConfigureAwait(false);
            }
            finally
            {
                tokenRegistration?.Dispose();
            }
        }

        /// <summary>Release the semaphore to allow a waiting task to enter. If the semaphore is completed, this
        /// operation just returns since there's no longer any task waiting to enter the semaphore.</summary>
        /// <exception name="SemaphoreFullException">Thrown when the semaphore is released too many times. It can't
        /// be released more times than the initial count provided to the constructor.</exception>
        internal void Release()
        {
            lock (_mutex)
            {
                if (_currentCount == _maxCount)
                {
                    throw new SemaphoreFullException($"semaphore maximum count of {_maxCount} already reached");
                }

                while (_queue.Count > 0)
                {
                    try
                    {
                        _queue.Dequeue().SetResult(true);
                        return;
                    }
                    catch
                    {
                        // Ignore, this can occur if WaitAsync is canceled.
                    }
                }

                // Increment the semaphore if there's no waiter.
                ++_currentCount;
            }
        }
    }
}
