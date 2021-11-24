// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    /// <summary>The AsyncQueue provides queuing functionality with an asynchronous dequeue function.</summary>
    internal class AsyncQueue<T> : IAsyncQueueValueTaskSource<T>
    {
        // TODO: remove pragma warning disable/restore once analyser is fixed.
        // It is necessary to call new() explicitly to execute the parameterless ctor of AsyncQueueCore, which is
        // synthesized from AsyncQueueCore fields defaults.
#pragma warning disable CA1805 // member is explicitly initialized to its default value
        private AsyncQueueCore<T> _queue = new();
#pragma warning restore CA1805

        internal bool TryComplete(Exception exception) => _queue.TryComplete(exception);

        internal void Enqueue(T value) => _queue.Enqueue(value);

        internal ValueTask<T> DequeueAsync(CancellationToken cancel) => _queue.DequeueAsync(this, cancel);

        void IAsyncQueueValueTaskSource<T>.Cancel() => _queue.TryComplete(new OperationCanceledException());

        T IValueTaskSource<T>.GetResult(short token) => _queue.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _queue.GetStatus(token);

        void IValueTaskSource<T>.OnCompleted(
                Action<object?> continuation,
                object? state, short token,
                ValueTaskSourceOnCompletedFlags flags) =>
            _queue.OnCompleted(continuation, state, token, flags);
    }
}
