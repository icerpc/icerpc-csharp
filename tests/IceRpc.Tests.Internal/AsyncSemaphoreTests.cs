// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    public class AsyncSemaphoreTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(0, 2)]
        [TestCase(0, int.MaxValue)]
        public void AsyncSemaphore_Constructor(int initialCount, int maxCount) =>
            _ = new AsyncSemaphore(initialCount, maxCount);

        [TestCase(-1, 1)]
        [TestCase(1, 0)]
        [TestCase(1, -1)]
        [TestCase(9, 5)]
        public void AsyncSemaphore_Constructor_Exception(int initialCount, int maxCount) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AsyncSemaphore(initialCount, maxCount));

        [TestCase(1, 1)]
        [TestCase(10, 1)]
        [TestCase(10, 10)]
        public async Task AsyncSemaphore_EnterAsync(int maxCount, int count)
        {
            var semaphore = new AsyncSemaphore(maxCount, maxCount);
            for (int i = 0; i < count; ++i)
            {
                await semaphore.EnterAsync();
            }
        }

        [TestCase(0, 10)]
        [TestCase(1, 10)]
        [TestCase(10, 10)]
        public async Task AsyncSemaphore_EnterAsyncWithInitialCount(int initialCount, int count)
        {
            var semaphore = new AsyncSemaphore(initialCount, int.MaxValue);
            var tasks = new Queue<Task>();
            for (int i = 0; i < count; ++i)
            {
                tasks.Enqueue(semaphore.EnterAsync().AsTask());
            }
            await Task.Delay(100);
            int completedCount = 0;
            foreach (Task task in tasks)
            {
                completedCount += task.IsCompleted ? 1 : 0;
            }
            Assert.That(completedCount, Is.EqualTo(initialCount));
            for (int i = 0; i < count; ++i)
            {
                semaphore.Release();
                await tasks.Dequeue();
            }
        }

        [Test]
        public void AsyncSemaphore_EnterAsync_Cancellation0()
        {
            var semaphore = new AsyncSemaphore(1, 1);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await semaphore.EnterAsync(source.Token));
        }

        [TestCase(0)]
        [TestCase(10)]
        public async Task AsyncSemaphore_EnterAsync_Cancellation1(int timeout)
        {
            var semaphore = new AsyncSemaphore(1, 1);
            using var source = new CancellationTokenSource(timeout);
            await semaphore.EnterAsync();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await semaphore.EnterAsync(source.Token));
        }

        [TestCase(0)]
        [TestCase(10)]
        public void AsyncSemaphore_EnterAsync_Cancellation2(int timeout)
        {
            var semaphore = new AsyncSemaphore(0, 1);
            using var source = new CancellationTokenSource(timeout);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await semaphore.EnterAsync(source.Token));
        }

        [TestCase(1, 1)]
        [TestCase(10, 1)]
        [TestCase(10, 10)]
        public async Task AsyncSemaphore_Release(int maxCount, int count)
        {
            var semaphore = new AsyncSemaphore(maxCount, maxCount);
            for (int i = 0; i < count; ++i)
            {
                await semaphore.EnterAsync();
            }
            for (int i = 0; i < count; ++i)
            {
                semaphore.Release();
            }
        }

        [TestCase(0, 1)]
        [TestCase(5, 10)]
        public async Task AsyncSemaphore_ReleaseAndEnter(int initialCount, int releaseCount)
        {
            var semaphore = new AsyncSemaphore(initialCount, int.MaxValue);
            for (int i = 0; i < releaseCount; ++i)
            {
                semaphore.Release();
            }
            for (int i = 0; i < releaseCount; ++i)
            {
                await semaphore.EnterAsync();
            }
        }

        [TestCase(1)]
        [TestCase(10)]
        public void AsyncSemaphore_Release_Exception(int maxCount)
        {
            var semaphore = new AsyncSemaphore(maxCount, maxCount);
            Assert.Throws<SemaphoreFullException>(() => semaphore.Release());
        }

        [Test]
        public void AsyncSemaphore_Complete()
        {
            var semaphore = new AsyncSemaphore(1, 1);
            semaphore.Complete(new InvalidOperationException());
        }

        [Test]
        public async Task AsyncSemaphore_Complete_ExceptionThrowing()
        {
            // Completing the semaphore should cause EnterAsync to throw the completion exception, other methods
            // shouldn't throw this exception.
            var semaphore = new AsyncSemaphore(1, 1);
            semaphore.Complete(new InvalidOperationException());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await semaphore.EnterAsync());
            Assert.DoesNotThrow(() => semaphore.Complete(new InvalidOperationException()));
            Assert.Throws<SemaphoreFullException>(() => semaphore.Release());

            // Completing the semaphore should cause Release to throw if the semaphore isn't full
            semaphore = new AsyncSemaphore(1, 1);
            await semaphore.EnterAsync();
            semaphore.Complete(new InvalidOperationException());
            Assert.DoesNotThrow(() => semaphore.Release());
        }
    }
}
