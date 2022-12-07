// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Internal;

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
    private TaskCompletionSource? _waitForReleaseSource;

    /// <summary>Initializes a new instance of the asynchronous semaphore with the given maximum number of times that
    /// the semaphore can be entered.</summary>
    /// <param name="initialCount">The initial number of time for the semaphore that can be entered.</param>
    /// <param name="maxCount">The maximum number of times the semaphore can be entered.</param>
    /// <exception cref="ArgumentOutOfRangeException">Raised if maxCount or initialCount is less than 0 or if maxCount
    /// is less than initialCount.</exception>
    internal AsyncSemaphore(int initialCount, int maxCount)
    {
        if (initialCount < 0)
        {
            throw new ArgumentOutOfRangeException($"The {nameof(initialCount)} cannot be less than 0.");
        }
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException($"The {nameof(maxCount)} cannot be less than 0.");
        }
        if (maxCount < initialCount)
        {
            throw new ArgumentOutOfRangeException($"The {nameof(maxCount)} cannot be less than the {nameof(initialCount)}.");
        }
        _currentCount = initialCount;
        _maxCount = maxCount;
    }

    /// <summary>Cancels the callers that are waiting to enter the semaphore. The given exception will be raised by
    /// the awaited EnterAsync operation.</summary>
    /// <param name="exception">The exception raised to notify the callers waiting to enter the semaphore of the
    /// cancellation.</param>
    internal void CancelAwaiters(Exception exception)
    {
        lock (_mutex)
        {
            foreach (ManualResetValueTaskCompletionSource<bool> source in _queue)
            {
                try
                {
                    source.SetException(exception);
                }
                catch
                {
                    // Ignore, the source might already be completed if canceled.
                }
            }
            _queue.Clear();
        }
    }

    /// <summary>Notifies the callers that are waiting to enter the semaphore that the semaphore is being
    /// terminated. The given exception will be raised by the awaited EnterAsync operation.</summary>
    /// <param name="exception">The exception raised to notify the callers waiting to enter the semaphore of the
    /// completion.</param>
    internal void Complete(Exception exception)
    {
        lock (_mutex)
        {
            if (_exception is not null)
            {
                return;
            }
            _exception = exception;
        }

        CancelAwaiters(exception);
    }

    /// <summary>Notifies the callers that are waiting to enter the semaphore that the semaphore is being
    /// terminated. The given exception will be raised by the awaited EnterAsync operation. In addition, this method
    /// waits for the semaphore to be fully released.</summary>
    /// <param name="exception">The exception raised to notify the callers waiting to enter the semaphore of the
    /// completion.</param>
    internal Task CompleteAndWaitAsync(Exception exception)
    {
        Complete(exception);

        Task waitForReleaseTask;
        lock (_mutex)
        {
            if (_waitForReleaseSource is not null)
            {
                return _waitForReleaseSource.Task;
            }

            if (_currentCount == _maxCount)
            {
                waitForReleaseTask = Task.CompletedTask;
            }
            else
            {
                _waitForReleaseSource = new();
                waitForReleaseTask = _waitForReleaseSource.Task;
            }
        }

        return waitForReleaseTask;
    }

    /// <summary>Asynchronously enter the semaphore. If the semaphore can't be entered, this method waits
    /// until the semaphore is released.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <exception cref="_exception">Raises the completion exception if the semaphore is completed.</exception>
    internal async ValueTask EnterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ManualResetValueTaskCompletionSource<bool> taskCompletionSource;
        CancellationTokenRegistration tokenRegistration = default;

        lock (_mutex)
        {
            if (_exception is not null)
            {
                throw ExceptionUtil.Throw(_exception);
            }

            if (_currentCount > 0)
            {
                Debug.Assert(_queue.Count == 0);
                --_currentCount;
                return;
            }

            // Don't auto reset the task completion source after obtaining the result. This is necessary to
            // ensure that the exception won't be cleared if the task is canceled.
            taskCompletionSource = new(autoReset: false);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tokenRegistration = cancellationToken.UnsafeRegister(
                    _ =>
                    {
                        lock (_mutex)
                        {
                            if (_queue.Contains(taskCompletionSource))
                            {
                                taskCompletionSource.SetException(new OperationCanceledException(cancellationToken));
                            }
                        }
                    },
                    null);
            }
            _queue.Enqueue(taskCompletionSource);
        }

        // It's safe to call the synchronous Dispose of the token registration because its callback doesn't block
        // (the ManualResetValueTaskCompletionSource continuation is always executed asynchronously).
        using CancellationTokenRegistration _ = tokenRegistration;
        await taskCompletionSource.ValueTask.ConfigureAwait(false);
    }

    /// <summary>Release the semaphore to allow a waiting task to enter. If the semaphore is completed, this
    /// operation just returns since there's no longer any task waiting to enter the semaphore.</summary>
    /// <exception cref="SemaphoreFullException">Thrown when the semaphore is released too many times. It can't
    /// be released more times than the initial count provided to the constructor.</exception>
    internal void Release()
    {
        lock (_mutex)
        {
            if (_currentCount == _maxCount)
            {
                throw new SemaphoreFullException($"The semaphore maximum count of '{_maxCount}' already reached.");
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
                    // Ignore, this can occur if EnterAsync is canceled.
                }
            }

            // Increment the semaphore if there's no waiter.
            ++_currentCount;

            if (_currentCount == _maxCount)
            {
                _waitForReleaseSource?.TrySetResult();
            }
        }
    }
}
