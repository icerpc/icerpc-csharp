// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    /// <summary>This interface is required because AsyncQueueCore is a struct and we can't reference a struct from a
    /// lambra expression. The struct would be copied. This is necessary for the implementation of
    /// SetException.</summary>
    internal interface IAsyncQueueValueTaskSource<T> : IValueTaskSource<T>
    {
        void SetException(Exception exception);
    }

    /// <summary>The AsyncQueueCore struct provides result queuing functionality to be used with the
    /// IValueTaskSource interface. It's useful for the Slic stream implementation to avoid allocating on the
    /// heap objects to support a ValueTask based ReceiveAsync.
    /// </summary>
    internal struct AsyncQueueCore<T>
    {
        internal bool IsSignaled
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);
                    return _source.GetStatus(_source.Version) != ValueTaskSourceStatus.Pending;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }
        }

        private volatile Exception? _exception;
        // Provide thread safety using a spin lock to avoid having to create another object on the heap. The
        // lock is used to protect the setting of the signal value or exception with the manual reset value
        // task source.
        private SpinLock _lock;
        // The result queue is only created when Queue() is called and if the result can't be set on the
        // source when a result is already set on the source.
        private Queue<T>? _queue;
        private ManualResetValueTaskSourceCore<T> _source = new() { RunContinuationsAsynchronously = true };
        private CancellationTokenRegistration _tokenRegistration;

        /// <summary>Signals the stream with a new exception.</summary>
        /// <param name="exception">The exception that will be raised by WaitAsync.</param>
        internal void Complete(Exception exception)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                if (_exception == null)
                {
                    _exception = exception;

                    // If the source isn't already signaled, signal completion by setting the exception.
                    // Otherwise if it's already signaled, a result is pending. In this case, we'll raise the
                    // exception the next time the queue is awaited. This is necessary because
                    // ManualResetValueTaskSourceCore is not thread safe and once an exception or result is
                    // set we can't call again SetXxx until the source's result or exception is consumed.
                    if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                    {
                        _source.SetException(exception);
                    }
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        internal T GetResult(short token)
        {
            Debug.Assert(token == _source.Version);

            _tokenRegistration.Dispose();
            _tokenRegistration = default;

            // Get the result.
            var result = default(T);
            Exception? exception = null;
            try
            {
                result = _source.GetResult(token);
            }
            catch (Exception ex)
            {
                // If the stream has been aborted, we let the exception go through and we don't reset the source.
                if (_exception != null)
                {
                    throw ExceptionUtil.Throw(_exception);
                }
                exception = ex;
            }

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                // Reseting the source must be done with the lock held because other threads are checking the source
                // status to figure out whether or not to set another result or exception on the source.
                _source.Reset();

                if (_queue != null && _queue.Count > 0)
                {
                    // If there are results queued, dequeue the result and set it on the source.
                    _source.SetResult(_queue.Dequeue());
                }
                else if (_exception != null)
                {
                    // If an exception is set, we set it on the source.
                    _source.SetException(_exception);
                }
                return exception != null ? throw ExceptionUtil.Throw(exception) : result!;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        internal ValueTaskSourceStatus GetStatus(short token)
        {
            Debug.Assert(token == _source.Version);
            return _source.GetStatus(token);
        }

        internal void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            Debug.Assert(token == _source.Version);
            _source.OnCompleted(continuation, state, token, flags);
        }

        internal void Queue(T result)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                {
                    // If the source isn't already signaled, signal completion by setting the result. The
                    // queue should be empty if the source is pending.
                    Debug.Assert(_queue == null || _queue.Count == 0);
                    _source.SetResult(result);
                }
                else if (_exception != null)
                {
                    // The stream is already signaled because it got aborted.
                    throw new InvalidOperationException("the stream is already signaled", _exception);
                }
                else
                {
                    // Create the queue if needed and queue the result.
                    _queue ??= new();
                    _queue.Enqueue(result);
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        internal void SetException(Exception exception)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                {
                    // If the source isn't already signaled, signal completion by setting the exception.
                    _source.SetException(exception);
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

        }

        internal void SetResult(T result)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                {
                    // If the source isn't already signaled, signal completion by setting the result.
                    _source.SetResult(result);
                }
                else
                {
                    Debug.Assert(_exception != null);
                    // The stream is already signaled because it got aborted.
                    throw new InvalidOperationException("the stream is already signaled", _exception);
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        internal ValueTask<T> WaitAsync(IAsyncQueueValueTaskSource<T> valueTaskSource, CancellationToken cancel)
        {
            if (cancel.CanBeCanceled)
            {
                Debug.Assert(_tokenRegistration == default);
                cancel.ThrowIfCancellationRequested();
                _tokenRegistration = cancel.Register(
                    () => valueTaskSource.SetException(new OperationCanceledException()));
            }
            return new ValueTask<T>(valueTaskSource, _source.Version);
        }
    }
}
