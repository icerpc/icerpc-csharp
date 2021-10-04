// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    /// <summary>A manual reset task completion source for ValueTask. It provides the same functionality as the
    /// TaskCompletionSource class but with ValueTask support instead. It's useful for hot code paths that
    /// require to minimize heap allocations required by the Task class. This class is NOT thread safe.</summary>
    internal class ManualResetValueTaskCompletionSource<T> : IValueTaskSource<T>
    {
        internal bool IsCompleted => _source.GetStatus(_source.Version) != ValueTaskSourceStatus.Pending;
        internal ValueTask<T> ValueTask => new(this, _source.Version);
        private ManualResetValueTaskSourceCore<T> _source;
        private readonly bool _autoReset;
        private readonly object _mutex = new();

        /// <summary>Initializes a new instance of ManualResetValueTaskCompletionSource with a boolean indicating
        /// if the source should be reset after the result is obtained. If the auto reset is disabled, the Reset
        /// method needs to be called explicitly before re-using the source.</summary>
        /// <param name="autoReset">The source is reset automatically after the result is retrieve.</param>
        internal ManualResetValueTaskCompletionSource(bool autoReset = true)
        {
            _autoReset = autoReset;
            _source.RunContinuationsAsynchronously = true;
        }

        internal void Reset()
        {
            lock (_mutex)
            {
                _source.Reset();
            }
        }

        internal void SetException(Exception exception)
        {
            lock (_mutex)
            {
                _source.SetException(exception);
            }
        }

        internal void SetResult(T value)
        {
            lock (_mutex)
            {
                _source.SetResult(value);
            }
        }

        T IValueTaskSource<T>.GetResult(short token)
        {
            bool isValid = token == _source.Version;
            try
            {
                return _source.GetResult(token);
            }
            finally
            {
                if (isValid && _autoReset)
                {
                    Reset();
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
