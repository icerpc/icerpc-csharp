// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    /// <summary>The AsyncQueue provides queuing functionality with a <see cref="ValueTask"/> asynchronous dequeue
    /// function. This class is public even though it's in the internal namespace. It's used by the coloc transport
    /// which is implemented in another assembly.</summary>
    internal class AsyncQueue<T> : IAsyncQueueValueTaskSource<T>
    {
        // TODO: remove pragma warning disable/restore once analyser is fixed.
        // It is necessary to call new() explicitly to execute the parameterless ctor of AsyncQueueCore, which is
        // synthesized from AsyncQueueCore fields defaults.
#pragma warning disable CA1805 // member is explicitly initialized to its default value
        private AsyncQueueCore<T> _queue = new();
#pragma warning restore CA1805

        /// <summary>Attempts to mark the queue as being completed, meaning no more elements will be queued. The
        /// exception will be raised by <see cref="Enqueue"/> if it's called after this call. It will also be called by
        /// <see cref="DequeueAsync"/> after the last element has been dequeued.</summary>
        /// <param name="exception">The exception indication why no more elements can be queued.</param>
        /// <returns><c>true</c> if the queue as been marked as completed, <c>false</c> if the queue was already
        /// completed.</returns>
        internal bool TryComplete(Exception exception) => _queue.TryComplete(exception);

        /// <summary>Enqueue a new element.</summary>
        /// <param name="value">The value of the element to enqueue</param>
        internal void Enqueue(T value) => _queue.Enqueue(value);

        /// <summary>Asynchronously dequeues an element.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns name="value">The value of the element to enqueue.</returns>
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
