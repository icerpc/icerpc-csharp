// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The SignaledStream abstract class provides signaling functionality using the
    /// IValueTaskSource interface. It's useful for stream implementations that depend on the connection
    /// for receiving data. The connection can easily signal the stream when new data is available.
    /// Signaling the stream and either be done with SetResult/SetException to signal a single value
    /// or values can be queued using QueueResult. QueueResult might require allocating a queue on the
    /// heap. Stream implementations will typically only use it when needed.
    /// </summary>
    internal abstract class SignaledStream<T> : NetworkStream, IValueTaskSource<T>
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
        // Provide thread safety using a spin lock to avoid having to create another object on the heap. The lock
        // is used to protect the setting of the signal value or exception with the manual reset value task source.
        private SpinLock _lock;
        // The result queue is only created when QueueResult() is called and if the result can't be set on the source
        // when a result is already set on the source.
        private Queue<T>? _resultQueue;
        private ManualResetValueTaskSourceCore<T> _source;
        private CancellationTokenRegistration _tokenRegistration;

        public override void AbortRead(StreamError errorCode)
        {
            // It's important to set the exception before completing the reads because WaitAsync expects the
            // exception to be set if reads are completed.
            SetException(new StreamAbortedException(errorCode));

            if (TrySetReadCompleted(shutdown: false))
            {
                if (IsStarted && !IsShutdown && errorCode != StreamError.ConnectionAborted)
                {
                    // Notify the peer of the read abort by sending a stop sending frame.
                    _ = SendStopSendingFrameAndShutdownAsync();
                }
                else
                {
                    // Shutdown the stream if not already done.
                    TryShutdown();
                }
            }

            async Task SendStopSendingFrameAndShutdownAsync()
            {
                try
                {
                    await SendStopSendingFrameAsync(errorCode).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TryShutdown();
            }
        }

        public override void AbortWrite(StreamError errorCode)
        {
            if (IsStarted && !IsShutdown && errorCode != StreamError.ConnectionAborted)
            {
                // Notify the peer of the write abort by sending a reset frame.
                _ = SendResetFrameAndCompleteWritesAsync();
            }
            else
            {
                TrySetWriteCompleted();
            }

            async Task SendResetFrameAndCompleteWritesAsync()
            {
                try
                {
                    await SendResetFrameAsync(errorCode).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TrySetWriteCompleted();
            }
        }

        protected SignaledStream(MultiStreamConnection connection, long streamId)
            : base(connection, streamId) => _source.RunContinuationsAsynchronously = true;

        protected SignaledStream(MultiStreamConnection connection, bool bidirectional)
            : base(connection, bidirectional) => _source.RunContinuationsAsynchronously = true;

        protected override void Shutdown()
        {
            base.Shutdown();

            // The stream might not be signaled if it's shutdown gracefully after receiving endStream. We
            // make sure to set the exception in this case to prevent WaitAsync calls to block.
            SetException(new StreamAbortedException(StreamError.StreamAborted));

            // Unregister the cancellation token callback
            _tokenRegistration.Dispose();
        }

        /// <summary>Queue a new result. Results are typically queued when stream buffering is enabled to receive
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
            if (ReadsCompleted && _exception == null)
            {
                // If reads are completed and no exception is set, it's probably because ReceiveAsync is called after
                // receiving the end stream flag.
                throw new InvalidOperationException("reads are completed");
            }

            if (cancel.CanBeCanceled)
            {
                Debug.Assert(_tokenRegistration == default);
                cancel.ThrowIfCancellationRequested();
                _tokenRegistration = cancel.Register(() =>
                {
                    // We don't use SetException here since the cancellation of WaitAsync isn't considered as
                    // an unrecoverable error.
                    bool lockTaken = false;
                    try
                    {
                        _lock.Enter(ref lockTaken);
                        if (_source.GetStatus(_source.Version) == ValueTaskSourceStatus.Pending)
                        {
                            // If the source isn't already signaled, signal completion by setting the exception.
                            _source.SetException(new OperationCanceledException(cancel));
                        }
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            _lock.Exit();
                        }
                    }
                });
            }
            return new ValueTask<T>(this, _source.Version);
        }

        private protected abstract Task SendResetFrameAsync(StreamError errorCode);

        private protected abstract Task SendStopSendingFrameAsync(StreamError errorCode);

        T IValueTaskSource<T>.GetResult(short token)
        {
            Debug.Assert(token == _source.Version);

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
                    throw _exception;
                }
                exception = ex;
            }

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
                return exception != null ? throw exception : result!;
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
