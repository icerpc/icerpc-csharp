// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Retry.Internal;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Retry.Tests;

public sealed class ResettablePipeReaderDecoratorTests
{
    [Test]
    public async Task Reset_resets_reader_decorator()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 100);
        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.End);
        sut.Complete();

        // Act
        sut.Reset();

        Assert.That(sut.IsResettable, Is.True);
        Assert.That(mock.CompleteCalled, Is.False);
        Assert.That(mock.CompleteException, Is.Null);
        Assert.That(mock.Consumed, Is.EqualTo(readResult.Buffer.Start));
        Assert.That(mock.CancelPendingReadCalled, Is.False);
        Assert.That(mock.Examined, Is.EqualTo(readResult.Buffer.End));
    }

    [Test]
    public void CancelPendingRead_cancels_decoratee()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 100);

        // Act
        sut.CancelPendingRead();

        Assert.That(mock.CancelPendingReadCalled, Is.True);
    }

    [Test]
    public async Task Examined_keeps_increasing()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 100);

        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.GetPosition(3), readResult.Buffer.GetPosition(4));
        sut.Complete();
        sut.Reset();

        // Act
        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.GetPosition(1), readResult.Buffer.GetPosition(2));
        sut.Complete();

        Assert.That(sut.IsResettable, Is.True);
        Assert.That(mock.CompleteCalled, Is.False);
        Assert.That(mock.CompleteException, Is.Null);
        Assert.That(mock.Consumed, Is.EqualTo(readResult.Buffer.Start));
        Assert.That(mock.CancelPendingReadCalled, Is.False);
        Assert.That(mock.Examined, Is.EqualTo(readResult.Buffer.GetPosition(4)));
    }

    [Test]
    public async Task Consume_slices_next_read_result()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 100);
        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.GetPosition(3), readResult.Buffer.GetPosition(4));

        // Act
        ReadResult slicedResult = await sut.ReadAsync();
        sut.Complete();

        Assert.That(slicedResult.Buffer.Length, Is.EqualTo(2));
        Assert.That(slicedResult.Buffer.FirstSpan[0], Is.EqualTo(4));
        Assert.That(mock.CompleteCalled, Is.False);
        Assert.That(mock.CompleteException, Is.Null);
        Assert.That(mock.Consumed, Is.EqualTo(readResult.Buffer.Start));
        Assert.That(mock.CancelPendingReadCalled, Is.False);
        Assert.That(mock.Examined, Is.EqualTo(readResult.Buffer.GetPosition(4)));
    }

    [Test]
    public async Task Consume_slices_next_read_result_when_non_resettable()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3, 4, 5 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 100);
        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.GetPosition(2), readResult.Buffer.GetPosition(3));
        sut.IsResettable = false;

        // Act
        ReadResult slicedResult = await sut.ReadAsync();
        sut.AdvanceTo(slicedResult.Buffer.GetPosition(2)); // 2 + 2 >= 3
        sut.Complete();

        Assert.That(slicedResult.Buffer.Length, Is.EqualTo(3));
        Assert.That(slicedResult.Buffer.FirstSpan[0], Is.EqualTo(3));
        Assert.That(mock.CompleteCalled, Is.True);
        Assert.That(mock.CompleteException, Is.Null);
        Assert.That(mock.Consumed, Is.EqualTo(slicedResult.Buffer.GetPosition(2)));
        Assert.That(mock.CancelPendingReadCalled, Is.False);
        Assert.That(mock.Examined, Is.EqualTo(readResult.Buffer.GetPosition(4)));
    }

    [Test]
    public async Task Reader_decorator_becomes_non_resettable_for_large_buffer()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 2);

        // Act
        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.End);
        sut.Complete();

        Assert.That(sut.IsResettable, Is.False);
        Assert.That(mock.CompleteCalled, Is.True);
        Assert.That(mock.CompleteException, Is.Null);
        Assert.That(mock.Consumed, Is.EqualTo(readResult.Buffer.End));
        Assert.That(mock.CancelPendingReadCalled, Is.False);
        Assert.That(mock.Examined, Is.EqualTo(readResult.Buffer.End));
    }

    [Test]
    public async Task Non_resettable_reader_decorator_is_pass_through()
    {
        var readResult = new ReadResult(
            new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }),
            false,
            false);

        var mock = new MockPipeReader(readResult);
        var sut = new ResettablePipeReaderDecorator(mock, maxBufferSize: 100);
        sut.IsResettable = false;

        // Act
        _ = await sut.ReadAsync();
        sut.AdvanceTo(readResult.Buffer.End);
        sut.Complete();

        Assert.That(mock.CompleteCalled, Is.True);
        Assert.That(mock.CompleteException, Is.Null);
        Assert.That(mock.Consumed, Is.EqualTo(readResult.Buffer.End));
        Assert.That(mock.CancelPendingReadCalled, Is.False);
        Assert.That(mock.Examined, Is.EqualTo(readResult.Buffer.End));
    }

    private class MockPipeReader : PipeReader
    {
        internal bool CompleteCalled { get; private set; }
        internal Exception? CompleteException { get; private set; }
        internal SequencePosition Consumed { get; private set; }
        internal bool CancelPendingReadCalled { get; private set; }
        internal SequencePosition Examined { get; private set; }
        private readonly ReadResult _readResult;

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            Consumed = consumed;
            Examined = examined;
        }

        public override void CancelPendingRead() => CancelPendingReadCalled = true;

        public override void Complete(Exception? exception)
        {
            CompleteCalled = true;
            CompleteException = exception;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken) => new(_readResult);
        public override bool TryRead(out ReadResult result)
        {
            result = _readResult;
            return true;
        }

        protected override ValueTask<ReadResult> ReadAtLeastAsyncCore(
            int minimumSize,
            CancellationToken cancellationToken) => new(_readResult);

        internal MockPipeReader(ReadResult readResult) => _readResult = readResult;
    }
}
