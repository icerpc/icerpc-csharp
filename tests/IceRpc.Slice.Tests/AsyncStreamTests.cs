// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Operations;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class AsyncStreamTests
{
    [Test]
    public void Dispose_completes_reader_when_iteration_never_started()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        stream.Dispose();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_is_idempotent()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        stream.Dispose();
        stream.Dispose();
        stream.Dispose();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Full_iteration_completes_reader()
    {
        byte[] payload = EncodeInt32Values(1, 2, 3);
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>(payload)));
        using IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        var items = new List<int>();
        await foreach (int item in stream)
        {
            items.Add(item);
        }

        Assert.That(items, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Break_during_iteration_completes_reader()
    {
        byte[] payload = EncodeInt32Values(1, 2, 3);
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>(payload)));
        using IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        await foreach (int item in stream)
        {
            break;
        }

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_during_iteration_throws_ObjectDisposedException_and_completes_reader()
    {
        // A pipe whose writer never produces data: ReadAsync blocks until we cancel.
        var pipe = new Pipe();
        var trackingReader = new TrackingPipeReader(pipe.Reader);
        IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator();
        ValueTask<bool> moveNext = enumerator.MoveNextAsync();

        // Give the iterator a chance to actually start its ReadAsync.
        await Task.Yield();

        stream.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () => await moveNext);

        await enumerator.DisposeAsync();
        pipe.Writer.Complete();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Caller_cancellation_token_throws_OperationCanceledException_and_completes_reader()
    {
        // A pipe whose writer never produces data: ReadAsync blocks until cancellation.
        var pipe = new Pipe();
        var trackingReader = new TrackingPipeReader(pipe.Reader);
        IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        using var cts = new CancellationTokenSource();

        // Pass the caller token through GetAsyncEnumerator; this is what `await foreach (... .WithCancellation(ct))`
        // does and it ends up bound to the [EnumeratorCancellation] parameter of EnumerateAsync.
        IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(cts.Token);
        ValueTask<bool> moveNext = enumerator.MoveNextAsync();

        // Give the iterator a chance to actually start its ReadAsync.
        await Task.Yield();

        cts.Cancel();

        OperationCanceledException? exception = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await moveNext);
        Assert.That(exception!.CancellationToken, Is.EqualTo(cts.Token));

        await enumerator.DisposeAsync();
        stream.Dispose();
        pipe.Writer.Complete();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_after_getting_enumerator_without_starting_iteration_completes_reader()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        _ = stream.GetAsyncEnumerator();
        stream.Dispose();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public void GetAsyncEnumerator_throws_on_second_call()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        using IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        _ = stream.GetAsyncEnumerator();

        Assert.That(() => stream.GetAsyncEnumerator(), Throws.InvalidOperationException);
    }

    [Test]
    public void Move_next_async_throws_after_dispose()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<int> stream = trackingReader.ToAsyncStream(
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        stream.Dispose();

        // GetAsyncEnumerator on a disposed stream is allowed; the disposed check happens on the first MoveNextAsync.
        IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator();
        Assert.That(async () => await enumerator.MoveNextAsync(), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Dispose_concurrent_with_first_move_next_completes_reader_exactly_once()
    {
        // Stress test for the Dispose / first MoveNextAsync race. We loop many times to maximize the chance of
        // exercising both inter-leavings: Dispose wins (iterator throws ObjectDisposedException before touching the
        // reader) and iterator wins (Dispose only signals cancellation; iterator's finally completes the reader).
        // In every case _reader.Complete must be called exactly once and MoveNextAsync must either complete normally
        // or throw ObjectDisposedException.

        const int iterations = 200;
        for (int i = 0; i < iterations; i++)
        {
            var pipe = new Pipe();
            var trackingReader = new TrackingPipeReader(pipe.Reader);
            IAsyncStream<int> stream = trackingReader.ToAsyncStream(
                (ref SliceDecoder decoder) => decoder.DecodeInt32(),
                elementSize: 4);

            IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator();

            var disposeTask = Task.Run(stream.Dispose);
            Task<bool> moveNextTask = Task.Run(async () => await enumerator.MoveNextAsync());

            await disposeTask;

            try
            {
                await moveNextTask;
            }
            catch (ObjectDisposedException)
            {
                // Expected when Dispose wins the race (or signals cancellation mid-read).
            }

            await enumerator.DisposeAsync();
            pipe.Writer.Complete();

            Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1), $"iteration {i}");
        }
    }

    private static byte[] EncodeInt32Values(params int[] values)
    {
        var writer = new ArrayBufferWriter<byte>();
        var encoder = new SliceEncoder(writer);
        foreach (int value in values)
        {
            encoder.EncodeInt32(value);
        }
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>A PipeReader wrapper that delegates everything to an inner reader and counts Complete calls.</summary>
    private sealed class TrackingPipeReader : PipeReader
    {
        private readonly PipeReader _inner;
        private int _completeCallCount;

        internal int CompleteCallCount => Volatile.Read(ref _completeCallCount);

        public override void AdvanceTo(SequencePosition consumed) => _inner.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _inner.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _inner.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            Interlocked.Increment(ref _completeCallCount);
            _inner.Complete(exception);
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(cancellationToken);

        public override bool TryRead(out ReadResult result) => _inner.TryRead(out result);

        internal TrackingPipeReader(PipeReader inner) => _inner = inner;
    }
}
