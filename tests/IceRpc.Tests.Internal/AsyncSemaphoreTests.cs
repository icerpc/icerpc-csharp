// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class AsyncSemaphoreTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void AsyncSemaphore_Constructor(int maxCount) => _ = new AsyncSemaphore(maxCount);

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void AsyncSemaphore_Constructor_Exception(int maxCount) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AsyncSemaphore(maxCount));

        [TestCase(1, 1)]
        [TestCase(10, 1)]
        [TestCase(10, 10)]
        public async Task AsyncSemaphore_EnterAsync(int maxCount, int count)
        {
            var semaphore = new AsyncSemaphore(maxCount);
            for (int i = 0; i < count; ++i)
            {
                await semaphore.EnterAsync();
            }
        }

        [Test]
        public void AsyncSemaphore_EnterAsync_Cancellation0()
        {
            var semaphore = new AsyncSemaphore(1);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await semaphore.EnterAsync(source.Token));
        }

        [TestCase(0)]
        [TestCase(10)]
        public async Task AsyncSemaphore_EnterAsync_Cancellation1(int timeout)
        {
            var semaphore = new AsyncSemaphore(1);
            using var source = new CancellationTokenSource(timeout);
            await semaphore.EnterAsync();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await semaphore.EnterAsync(source.Token));
        }

        [TestCase(1, 1)]
        [TestCase(10, 1)]
        [TestCase(10, 10)]
        public async Task AsyncSemaphore_Release(int maxCount, int count)
        {
            var semaphore = new AsyncSemaphore(maxCount);
            for (int i = 0; i < count; ++i)
            {
                await semaphore.EnterAsync();
            }
            for (int i = 0; i < count; ++i)
            {
                semaphore.Release();
            }
        }

        [TestCase(1)]
        [TestCase(10)]
        public void AsyncSemaphore_Release_Exception(int maxCount)
        {
            var semaphore = new AsyncSemaphore(maxCount);
            Assert.Throws<SemaphoreFullException>(() => semaphore.Release());
        }

        [Test]
        public void AsyncSemaphore_Complete()
        {
            var semaphore = new AsyncSemaphore(1);
            semaphore.Complete(new InvalidOperationException());
        }

        [Test]
        public async Task AsyncSemaphore_Complete_ExceptionThrowing()
        {
            // Completing the semaphore should cause EnterAsync to throw the completion exception, other methods
            // shouldn't throw this exception.
            var semaphore = new AsyncSemaphore(1);
            semaphore.Complete(new InvalidOperationException());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await semaphore.EnterAsync());
            Assert.DoesNotThrow(() => semaphore.Complete(new InvalidOperationException()));
            Assert.Throws<SemaphoreFullException>(() => semaphore.Release());

            // Completing the semaphore should cause Release to throw if the semaphore isn't full
            semaphore = new AsyncSemaphore(1);
            await semaphore.EnterAsync();
            semaphore.Complete(new InvalidOperationException());
            Assert.DoesNotThrow(() => semaphore.Release());
        }
    }
}
