// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    /// <summary>This interface is required because AsyncQueueCore is a struct and we can't reference a struct from the
    /// function registered with the cancellation token to cancel the asynchronous dequeue call.</summary>
    internal interface IAsyncQueueValueTaskSource<T> : IValueTaskSource<T>
    {
        /// <summary>Cancels the asynchronous dequeue call.</summary>
        void Cancel();
    }

    /// <summary>The AsyncQueueCore struct provides result queuing functionality to be used with the IValueTaskSource
    /// interface. It's useful for the Slic stream implementation to avoid allocating on the heap objects to support a
    /// ValueTask based ReceiveAsync.
    /// </summary>
    internal struct AsyncQueueCore<T>
    {
        private Exception? _exception;
        // Provide thread safety using a spin lock to avoid having to create another object on the heap. The lock is
        // used to protect the setting of the signal value or exception with the manual reset value task source.
        private SpinLock _lock;
        // The result queue is only created when Enqueue() is called and if the result can't be set on the source when a
        // result is already set on the source.
        private Queue<T>? _queue;
        private ManualResetValueTaskSourceCore<T> _source = new() { RunContinuationsAsynchronously = true };
        private CancellationTokenRegistration _tokenRegistration;

        /// <summary>Complete the pending <see cref="DequeueAsync"/> and discard queued items.</summary>
        internal bool TryComplete(Exception exception)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_exception != null)
                {
                    return false;
                }

                _exception = exception;

                // If the source isn't already signaled, signal completion by setting the exception. Otherwise if
                // it's already signaled, a result is pending. In this case, we'll raise the exception the next time
                // the queue is awaited. This is necessary because ManualResetValueTaskSourceCore is not thread safe
                // and once an exception or result is set we can't call again SetXxx until the source's result or
                // exception is consumed.
                if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                {
                    _source.SetException(exception);
                }
                return true;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
        }

        internal void Enqueue(T value)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_exception == null)
                {
                    if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                    {
                        // If the source is pending, set the result on the source result. The  queue should be empty if
                        // the source is pending.
                        Debug.Assert(_queue == null || _queue.Count == 0);
                        _source.SetResult(value);
                    }
                    else
                    {
                        // Create the queue if needed and queue the result. If will be consumed once the source's result
                        // is consumed.
                        _queue ??= new();
                        _queue.Enqueue(value);
                    }
                }
                else
                {
                    throw _exception;
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
            T? result;
            try
            {
                result = _source.GetResult(token);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex);
            }

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_exception != null)
                {
                    throw ExceptionUtil.Throw(_exception);
                }

                // Reseting the source must be done with the lock held because other threads are checking the source
                // status to figure out whether or not to set another result or exception on the source.
                _source.Reset();

                // If there results are queued, dequeue the result and set it on the source.
                if (_queue != null && _queue.Count > 0)
                {
                    _source.SetResult(_queue.Dequeue());
                }

                return result;
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

        internal ValueTask<T> DequeueAsync(IAsyncQueueValueTaskSource<T> valueTaskSource, CancellationToken cancel)
        {
            if (cancel.CanBeCanceled)
            {
                Debug.Assert(_tokenRegistration == default);
                cancel.ThrowIfCancellationRequested();
                _tokenRegistration = cancel.Register(valueTaskSource.Cancel);
            }
            return new ValueTask<T>(valueTaskSource, _source.Version);
        }
    }
}
