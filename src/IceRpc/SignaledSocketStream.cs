// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace IceRpc
{
    /// <summary>The SignaledSocketStream abstract class provides signaling functionality using the
    /// IValueTaskSource interface. It's useful for stream implementations that depend on the socket
    /// for receiving data. The socket can easily signal the stream when new data is available.
    /// Signaling the stream and either be done with SetResult/SetException to signal a single value
    /// or values can be queued using QueueResult. QueueResult might require allocating a queue on the
    /// heap. Stream implementations will typically only use it when needed.
    /// </summary>
    internal abstract class SignaledSocketStream<T> : SocketStream, IValueTaskSource<T>
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

        private Exception? _exception;
        // Provide thread safety using a spin lock to avoid having to create another object on the heap. The lock
        // is used to protect the setting of the signal value or exception with the manual reset value task source.
        private SpinLock _lock;
        // The result queue is only created when QueueResult() is called and if the result can't be set on the source
        // when a result is already set on the source.
        private Queue<T>? _resultQueue;
        private ManualResetValueTaskSourceCore<T> _source;
        private CancellationTokenRegistration _tokenRegistration;
        private static readonly Exception _disposedException =
            new ObjectDisposedException(nameof(SignaledSocketStream<T>));

        /// <summary>Aborts the stream.</summary>
        public override void Abort(Exception ex)
        {
            base.Abort(ex);
            SetException(ex);
        }

        protected SignaledSocketStream(MultiStreamSocket socket, long streamId)
            : base(socket, streamId) => _source.RunContinuationsAsynchronously = true;

        protected SignaledSocketStream(MultiStreamSocket socket, bool bidirectional, bool control)
            : base(socket, bidirectional, control) => _source.RunContinuationsAsynchronously = true;

        protected override void Shutdown()
        {
            base.Shutdown();

            // Ensure the stream signaling fails after destruction of the stream.
            SetException(_disposedException);

            // Unregister the cancellation token callback
            _tokenRegistration.Dispose();
        }

        /// <summary>Queue a new result. Results are typically queue when stream buffering is enabled to receive
        /// stream data.</summary>
        /// <param name="result">The result to queue.</param>
        protected void QueueResult(T result)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                {
                    // If the source isn't already signaled, signal completion by setting the result. The queue
                    // should be empty if the source is pending.
                    Debug.Assert(_resultQueue == null || _resultQueue.Count == 0);
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
                    _resultQueue ??= new();
                    _resultQueue.Enqueue(result);
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

        /// <summary>Signals the stream with a new exception.</summary>
        /// <param name="exception">The exception that will be raised by WaitAsync.</param>
        protected void SetException(Exception exception)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_exception == null)
                {
                    _exception = exception;

                    // If the source isn't already signaled, signal completion by setting the exception. Otherwise
                    // if it's already signaled, a result is pending. In this case, we'll raise the exception the
                    // next time the signal is awaited. This is necessary because ManualResetValueTaskSourceCore is
                    // not thread safe and once an exception or result is set we can't call again SetXxx until the
                    // source's result or exception is consumed.
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

        /// <summary>Signals the stream with a new result.</summary>
        /// <param name="result">The result that will be returned by WaitAsync.</param>
        protected void SetResult(T result)
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

        /// <summary>Wait for the stream to be signaled with a result or exception.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The value used to signaled the stream.</return>
        protected ValueTask<T> WaitAsync(CancellationToken cancel = default)
        {
            if (cancel.CanBeCanceled)
            {
                Debug.Assert(_tokenRegistration == default);
                cancel.ThrowIfCancellationRequested();
                _tokenRegistration = cancel.Register(() => SetException(new OperationCanceledException(cancel)));
            }
            return new ValueTask<T>(this, _source.Version);
        }

        T IValueTaskSource<T>.GetResult(short token)
        {
            Debug.Assert(token == _source.Version);

            // Get the result. This will throw if the stream has been aborted. In this case, we let the
            // exception go through and we don't reset the source.
            T result = _source.GetResult(token);

            // Reset the source to allow the stream to be signaled again. It's important to dispose the
            // registration without the lock held since Dispose() might block until the cancellation callback
            // is completed if the cancellation callback is running (and potentially trying to acquire the
            // lock to set the exception).
            _tokenRegistration.Dispose();
            _tokenRegistration = default;

            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                // Reseting the source must be done with the lock held because other threads are
                // checking the source status to figure out whether or not to set another result
                // or exception on the source.
                _source.Reset();

                if (_resultQueue != null && _resultQueue.Count > 0)
                {
                    // If there are results queued, dequeue the result and set it on the source.
                    _source.SetResult(_resultQueue.Dequeue());
                }
                else if (_exception != null)
                {
                    // If an exception is set, we set it on the source.
                    _source.SetException(_exception);
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

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token)
        {
            Debug.Assert(token == _source.Version);
            return _source.GetStatus(token);
        }

        void IValueTaskSource<T>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            Debug.Assert(token == _source.Version);
            _source.OnCompleted(continuation, state, token, flags);
        }
    }
}
