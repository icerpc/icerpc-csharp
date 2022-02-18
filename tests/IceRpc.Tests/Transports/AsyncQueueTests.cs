// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Transports.Internal.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class AsyncQueueTests
{
    [Test]
    public void AsyncQueue_TryComplete()
    {
        var queue = new AsyncQueue<bool>();
        Assert.That(queue.TryComplete(new InvalidOperationException()), Is.True);
        Assert.That(queue.TryComplete(new InvalidOperationException()), Is.False);
    }

    [Test]
    public void AsyncQueue_TryCompleteAfterEnqueue()
    {
        var queue = new AsyncQueue<bool>();
        queue.Enqueue(false);
        Assert.That(queue.TryComplete(new InvalidOperationException()), Is.True);
        Assert.That(queue.TryComplete(new InvalidOperationException()), Is.False);
    }

    [TestCase(1)]
    [TestCase(10)]
    public void AsyncQueue_Enqueue(int count)
    {
        var queue = new AsyncQueue<bool>();
        for (int i = 0; i < count; ++i)
        {
            queue.Enqueue(false);
        }
    }

    [Test]
    public void AsyncQueue_EnqueueException()
    {
        var queue = new AsyncQueue<bool>();
        queue.TryComplete(new InvalidOperationException());
        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(false));
    }

    [Test]
    public async Task AsyncQueue_DequeueAsync_AsyncCompletion()
    {
        var queue = new AsyncQueue<int>();
        ValueTask<int> valueTask = queue.DequeueAsync(default);
        Assert.That(valueTask.IsCompleted, Is.False);
        queue.Enqueue(13);
        Assert.That(await valueTask, Is.EqualTo(13));
    }

    [Test]
    public async Task AsyncQueue_DequeueAsync_SyncCompletion()
    {
        var queue = new AsyncQueue<int>();
        queue.Enqueue(13);
        ValueTask<int> valueTask = queue.DequeueAsync(default);
        Assert.That(valueTask.IsCompleted, Is.True);
        Assert.That(await valueTask, Is.EqualTo(13));
    }

    [Test]
    public void AsyncQueue_DequeueAsync_SyncCancellation()
    {
        var queue = new AsyncQueue<int>();
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => queue.DequeueAsync(source.Token).AsTask());

        // The queue is completed if DequeueAsync is canceled.
        Assert.Throws<OperationCanceledException>(() => queue.Enqueue(13));
    }

    [Test]
    public void AsyncQueue_DequeueAsync_AsyncCancellation()
    {
        var queue = new AsyncQueue<int>();
        using var source = new CancellationTokenSource();
        ValueTask<int> valueTask = queue.DequeueAsync(source.Token);
        Assert.That(valueTask.IsCompleted, Is.False);
        source.Cancel();
        Assert.That(valueTask.IsCompleted, Is.True);
        Assert.ThrowsAsync<OperationCanceledException>(() => valueTask.AsTask());

        // The queue is completed if DequeueAsync is canceled.
        Assert.Throws<OperationCanceledException>(() => queue.Enqueue(13));
    }
}
