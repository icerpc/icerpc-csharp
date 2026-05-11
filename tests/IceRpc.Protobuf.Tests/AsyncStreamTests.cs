// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Protobuf.RpcMethods.Internal;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class AsyncStreamTests
{
    [Test]
    public void Dispose_completes_reader_when_iteration_never_started()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(StringValue.Parser, maxMessageLength: 1024);

        stream.Dispose();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_is_idempotent()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(StringValue.Parser, maxMessageLength: 1024);

        stream.Dispose();
        stream.Dispose();
        stream.Dispose();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Full_iteration_completes_reader()
    {
        var trackingReader = new TrackingPipeReader(EncodeStringValues("a", "b", "c"));
        using IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(
            StringValue.Parser,
            maxMessageLength: 1024);

        var items = new List<string>();
        await foreach (StringValue item in stream)
        {
            items.Add(item.Value);
        }

        Assert.That(items, Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Break_during_iteration_completes_reader()
    {
        var trackingReader = new TrackingPipeReader(EncodeStringValues("a", "b", "c"));
        using IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(
            StringValue.Parser,
            maxMessageLength: 1024);

        await foreach (StringValue item in stream)
        {
            break;
        }

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_during_iteration_unblocks_pending_read_and_completes_reader_once()
    {
        // A pipe whose writer never produces data: ReadAsync blocks until we cancel.
        var pipe = new Pipe();
        var trackingReader = new TrackingPipeReader(pipe.Reader);
        IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(StringValue.Parser, maxMessageLength: 1024);

        IAsyncEnumerator<StringValue> enumerator = stream.GetAsyncEnumerator();
        ValueTask<bool> moveNext = enumerator.MoveNextAsync();

        // Give the iterator a chance to actually start its ReadAsync.
        await Task.Yield();

        stream.Dispose();

        // The pending ReadAsync is unblocked via CancelPendingRead through our linked CTS; the iterator's finally
        // calls Complete. MoveNextAsync should return false (graceful end-of-stream).
        bool moved = await moveNext;
        Assert.That(moved, Is.False);

        await enumerator.DisposeAsync();
        pipe.Writer.Complete();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Caller_cancellation_token_unblocks_pending_read_and_completes_reader()
    {
        // A pipe whose writer never produces data: ReadAsync blocks until cancellation.
        var pipe = new Pipe();
        var trackingReader = new TrackingPipeReader(pipe.Reader);
        IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(StringValue.Parser, maxMessageLength: 1024);

        using var cts = new CancellationTokenSource();

        // Pass the caller token through GetAsyncEnumerator; this is what `await foreach (... .WithCancellation(ct))`
        // does and it ends up bound to the [EnumeratorCancellation] parameter of EnumerateAsync.
        IAsyncEnumerator<StringValue> enumerator = stream.GetAsyncEnumerator(cts.Token);
        ValueTask<bool> moveNext = enumerator.MoveNextAsync();

        // Give the iterator a chance to actually start its ReadAsync.
        await Task.Yield();

        cts.Cancel();

        // The pending ReadAsync is unblocked through the linked CTS; the iterator's finally completes the reader.
        // MoveNextAsync returns false (graceful end-of-stream) since the OCE is swallowed by the iterator.
        bool moved = await moveNext;
        Assert.That(moved, Is.False);

        await enumerator.DisposeAsync();
        stream.Dispose();
        pipe.Writer.Complete();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_after_getting_enumerator_without_starting_iteration_completes_reader()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(StringValue.Parser, maxMessageLength: 1024);

        _ = stream.GetAsyncEnumerator();
        stream.Dispose();

        Assert.That(trackingReader.CompleteCallCount, Is.EqualTo(1));
    }

    [Test]
    public void GetAsyncEnumerator_throws_on_second_call()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        using IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(
            StringValue.Parser,
            maxMessageLength: 1024);

        _ = stream.GetAsyncEnumerator();

        Assert.That(() => stream.GetAsyncEnumerator(), Throws.InvalidOperationException);
    }

    [Test]
    public void GetAsyncEnumerator_throws_after_dispose()
    {
        var trackingReader = new TrackingPipeReader(PipeReader.Create(new ReadOnlySequence<byte>([0])));
        IAsyncStream<StringValue> stream = trackingReader.ToAsyncStream(StringValue.Parser, maxMessageLength: 1024);

        stream.Dispose();

        Assert.That(() => stream.GetAsyncEnumerator(), Throws.InstanceOf<ObjectDisposedException>());
    }

    private static PipeReader EncodeStringValues(params string[] values)
    {
        return EnumerateAsync().ToPipeReader();

        async IAsyncEnumerable<StringValue> EnumerateAsync()
        {
            // An async iterator method must contain at least one await.
            await Task.CompletedTask;

            foreach (string value in values)
            {
                yield return new StringValue { Value = value };
            }
        }
    }

    /// <summary>A PipeReader wrapper that delegates everything to an inner reader and counts Complete calls.</summary>
    private sealed class TrackingPipeReader : PipeReader
    {
        private readonly PipeReader _inner;

        internal int CompleteCallCount { get; private set; }

        public override void AdvanceTo(SequencePosition consumed) => _inner.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _inner.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _inner.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            CompleteCallCount++;
            _inner.Complete(exception);
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(cancellationToken);

        public override bool TryRead(out ReadResult result) => _inner.TryRead(out result);

        internal TrackingPipeReader(PipeReader inner) => _inner = inner;
    }
}
