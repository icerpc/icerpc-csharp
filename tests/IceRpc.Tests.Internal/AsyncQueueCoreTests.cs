// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Threading.Tasks.Sources;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class AsyncQueueCoreTests
    {
        [Test]
        public async Task AsyncQueueCore_IsCompleted()
        {
            var queue = new AsyncQueue();
            ValueTask<bool> valueTask = queue.DequeueAsync(default);
            Assert.That(valueTask.IsCompleted, Is.False);
            Assert.That(queue.IsGetStatusCalled, Is.True);
            Assert.That(queue.IsGetResultCalled, Is.False);

            queue.Enqueue(false);
            _ = await valueTask;
        }

        [Test]
        public async Task AsyncQueueCore_GetResultCalled_SyncDequeue()
        {
            var queue = new AsyncQueue();
            queue.Enqueue(false);
            _ = await queue.DequeueAsync(default);

            Assert.That(queue.IsGetStatusCalled, Is.True);
            Assert.That(queue.IsGetResultCalled, Is.True);
        }

        [Test]
        public async Task AsyncQueueCore_GetResultCalled_AsyncDequeue()
        {
            var queue = new AsyncQueue();
            ValueTask<bool> task = queue.DequeueAsync(default);
            queue.Enqueue(false);
            Assert.That(queue.IsGetResultCalled, Is.False);
            await task;

            Assert.That(queue.IsGetStatusCalled, Is.True);
            Assert.That(queue.IsGetResultCalled, Is.True);
        }

        [Test]
        public void AsyncQueueCore_GetResultCalled_AsTask_SyncDequeue()
        {
            var queue = new AsyncQueue();
            queue.Enqueue(false);
            _ = queue.DequeueAsync(default).AsTask();

            Assert.That(queue.IsGetStatusCalled, Is.True);
            Assert.That(queue.IsGetResultCalled, Is.True);
        }

        [Test]
        public void AsyncQueueCore_GetResultCalled_AsTask_AsyncDequeue()
        {
            var queue = new AsyncQueue();
            Task task = queue.DequeueAsync(default).AsTask();
            queue.Enqueue(false);
            task.Wait();

            Assert.That(queue.IsGetStatusCalled, Is.True);
            Assert.That(queue.IsGetResultCalled, Is.True);
        }

        [Test]
        public void AsyncQueueCore_GetResultCalled_Cancellation()
        {
            var queue = new AsyncQueue();
            using var source = new CancellationTokenSource();
            ValueTask<bool> valueTask = queue.DequeueAsync(source.Token);
            source.Cancel();
            Assert.That(queue.IsCancelCalled, Is.True);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await valueTask);
            Assert.That(queue.IsGetStatusCalled, Is.True);
            Assert.That(queue.IsGetResultCalled, Is.True);
        }

        private class AsyncQueue : IAsyncQueueValueTaskSource<bool>
        {
            internal bool IsGetResultCalled { get; private set; }
            internal bool IsGetStatusCalled { get; private set; }
            internal bool IsCancelCalled { get; private set; }

#pragma warning disable CA1805 // member is explicitly initialized to its default value
            private AsyncQueueCore<bool> _queue = new();
#pragma warning restore CA1805

            internal void Enqueue(bool value) => _queue.Enqueue(value);

            internal ValueTask<bool> DequeueAsync(CancellationToken cancel) => _queue.DequeueAsync(this, cancel);

            internal bool TryComplete(Exception exception) => _queue.TryComplete(exception);

            void IAsyncQueueValueTaskSource<bool>.Cancel()
            {
                IsCancelCalled = true;
                _queue.TryComplete(new OperationCanceledException());
            }

            bool IValueTaskSource<bool>.GetResult(short token)
            {
                IsGetResultCalled = true;
                return _queue.GetResult(token);
            }

            ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
            {
                IsGetStatusCalled = true;
                return _queue.GetStatus(token);
            }

            void IValueTaskSource<bool>.OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) =>
                _queue.OnCompleted(continuation, state, token, flags);
        }
    }
}
