// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

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
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => queue.DequeueAsync(cts.Token).AsTask());

        // The queue is completed if DequeueAsync is canceled.
        Assert.Throws<OperationCanceledException>(() => queue.Enqueue(13));
    }

    [Test]
    public void AsyncQueue_DequeueAsync_AsyncCancellation()
    {
        var queue = new AsyncQueue<int>();
        using var cts = new CancellationTokenSource();
        ValueTask<int> valueTask = queue.DequeueAsync(cts.Token);
        Assert.That(valueTask.IsCompleted, Is.False);
        cts.Cancel();
        Assert.That(valueTask.IsCompleted, Is.True);
        Assert.ThrowsAsync<OperationCanceledException>(() => valueTask.AsTask());

        // The queue is completed if DequeueAsync is canceled.
        Assert.Throws<OperationCanceledException>(() => queue.Enqueue(13));
    }
}
