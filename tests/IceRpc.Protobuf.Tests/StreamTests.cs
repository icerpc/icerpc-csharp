// Copyright (c) ZeroC, Inc.

using IceRpc.Protobuf.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Protobuf.Tests;

public class StreamTests
{
    /// <summary>Test that canceling the iteration using an injected cancellation token completes the pipe reader from
    /// which the stream elements are being decoded.</summary>
    [Test]
    public async Task Decoding_completes_when_iteration_is_canceled()
    {
        // Arrange
        var pipe = new Pipe();
        await EncodeDataAsync(pipe.Writer);

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        using var cts = new CancellationTokenSource();
        int count = 0;

        IAsyncEnumerable<InputMessage> values = payload.ToAsyncEnumerable(InputMessage.Parser, 16 * 1024, default);

        // Act
        await foreach (InputMessage value in values.WithCancellation(cts.Token))
        {
            count++;
            if (value.P2 == 1)
            {
                // It's also ok to just abandon the iteration (tested in another test).
                cts.Cancel();
            }
        }

        // Assert
        Assert.That(count, Is.EqualTo(2)); // read 2 elements
        Assert.That(() => payload.Completed, Is.Null);
        Assert.That(async () => (await pipe.Writer.FlushAsync()).IsCompleted, Is.True);

        // Cleanup
        pipe.Writer.Complete();

        static async Task EncodeDataAsync(PipeWriter writer)
        {
            for (int i = 0; i < 10; i++)
            {
                var inputMessage = new InputMessage
                {
                    P1 = $"message-{i}",
                    P2 = i
                };
                var payload = inputMessage.EncodeAsLengthPrefixedMessage(new PipeOptions());
                await payload.CopyToAsync(writer);
            }
            await writer.FlushAsync();
        }
    }

    /// <summary>Tests that canceling the iteration while the decode function is waiting to read data, cancels
    /// the read operation and completes the enumerable and pipe reader.</summary>
    [Test]
    public async Task Decoding_completes_when_enumerator_read_is_canceled()
    {
        // Arrange
        var pipe = new Pipe();
        var payload = new PayloadPipeReaderDecorator(pipe.Reader);
        payload.HoldRead = true;

        using var cts = new CancellationTokenSource();

        IAsyncEnumerable<InputMessage> values = payload.ToAsyncEnumerable(InputMessage.Parser, 16 * 1024, default);

        await using var enumerator = values.WithCancellation(cts.Token).GetAsyncEnumerator();

        var moveNextAwaitable = enumerator.MoveNextAsync();
        await payload.ReadCalled;

        // Act
        cts.Cancel();

        // Assert
        Assert.That(payload.IsReadCanceled, Is.True);
        Assert.That(() => payload.Completed, Is.Null);

        pipe.Writer.Complete();
    }

    /// <summary>Test that the payload is completed successfully after the async enumerable decoding throws
    /// <see cref="InvalidDataException" />.</summary>
    [Test]
    public async Task Decode_stream_containing_invalid_data_completes_payload()
    {
        // Arrange
        var pipe = new Pipe();

        var inputMessage = new InputMessage()
        {
            P1 = $"Message-1",
            P2 = 2
        };
        var reader = inputMessage.EncodeAsLengthPrefixedMessage(new PipeOptions());
        await reader.CopyToAsync(pipe.Writer);
        reader.Complete();
        // Invalid data IceRpc don't support compressed messages.
        pipe.Writer.Write(new byte[] { 1 });
        await pipe.Writer.FlushAsync();
        pipe.Writer.Complete();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        IAsyncEnumerable<InputMessage> values = payload.ToAsyncEnumerable(InputMessage.Parser, 16 * 1024, default);
        await using IAsyncEnumerator<InputMessage> enumerator = values.GetAsyncEnumerator();

        // Act/Assert
        Assert.That(enumerator.MoveNextAsync, Throws.Nothing);
        Assert.That(enumerator.MoveNextAsync, Throws.InstanceOf<InvalidDataException>());
        Assert.That(async () => await payload.Completed, Is.Null);
    }

    /// <summary>Verifies that we correctly encode an async enumerable of protobuf messages.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_and_decode_stream_of_protobuf_messages(int size, int yieldThreshold)
    {
        // Arrange
        InputMessage[] expected = Enumerable
            .Range(0, size)
            .Select(i => new InputMessage()
                {
                    P1 = $"Message-{i}",
                    P2 = i,
                }).ToArray();

        // Act
        var payload = GetDataAsync(size).ToPipeReader();

        // Assert
        Assert.That(async () => await DecodeDataAsync(payload), Is.EqualTo(expected));

        async IAsyncEnumerable<InputMessage> GetDataAsync(int size)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await Task.Yield();
                }
                yield return new InputMessage()
                {
                    P1 = $"Message-{i}",
                    P2 = i,
                };
            }
        }

        async Task<InputMessage[]> DecodeDataAsync(PipeReader payload)
        {
            var inputMessages = new List<InputMessage>();
            await foreach(var message in payload.ToAsyncEnumerable(InputMessage.Parser, 16 * 1024, default))
            {
                inputMessages.Add(message);
            }
            return inputMessages.ToArray();
        }
    }

    [Test]
    public async Task Enumerable_pipe_reader_completion_disposes_the_enumerator()
    {
        // Arrange
        var enumerable = new TestAsyncEnumerable();

        var payload = enumerable.ToPipeReader();

        // Act
        payload.Complete();

        // Assert
        await enumerable.Enumerator.DisposeCalled;
    }

    /// <summary>Tests that calling Complete on the pipe reader created from the enumerable correctly cancels the
    /// enumerable iteration.</summary>
    [Test]
    public async Task Enumerable_pipe_reader_completion_cancels_enumerator()
    {
        // Arrange
        var canceledTcs = new TaskCompletionSource();
        var payload = GetDataAsync(default).ToPipeReader();

        ReadResult readResult = await payload.ReadAsync();
        payload.AdvanceTo(readResult.Buffer.End);

        // Act
        payload.Complete();

        // Assert
        await canceledTcs.Task;

        async IAsyncEnumerable<InputMessage> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new InputMessage
            {
                P1 = $"message-1",
                P2 = 2,
            };

            try
            {
                await Task.Delay(-1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                canceledTcs.SetResult();
                throw;
            }
        }
    }

    /// <summary>Tests that calling CancelPendingRead on the pipe reader created from the enumerable correctly cancels
    /// the enumerable iteration.</summary>
    [Test]
    public async Task Enumerable_pipe_reader_cancel_pending_read_cancels_enumerator()
    {
        // Arrange
        var canceledTcs = new TaskCompletionSource();
        var payload = GetDataAsync(default).ToPipeReader();

        ValueTask<ReadResult> readResultTask = payload.ReadAsync();

        // Act
        payload.CancelPendingRead();

        // Assert
        ReadResult readResult = await readResultTask;
        Assert.That(readResult.IsCanceled, Is.True);
        await canceledTcs.Task;

        // Cleanup
        payload.Complete();

        async IAsyncEnumerable<InputMessage> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(-1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                canceledTcs.SetResult();
                throw;
            }

            yield return new InputMessage
            {
                P1 = $"message-1",
                P2 = 2,
            };
        }
    }

    [Test]
    public async Task Enumerable_pipe_reader_read_fails_if_enumerable_throws_exception()
    {
        // Arrange
        var exception = new Exception();
        var payload = GetDataAsync().ToPipeReader();

        ReadResult readResult = await payload.ReadAsync();
        payload.AdvanceTo(readResult.Buffer.End);

        // Act/Assert
        Exception? throwException = Assert.ThrowsAsync<Exception>(async () => await payload.ReadAsync());
        Assert.That(throwException, Is.EqualTo(exception));

        // Cleanup
        payload.Complete();

        async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            yield return new InputMessage
            {
                P1 = $"message-1",
                P2 = 2,
            };
            await Task.Yield();
            throw exception;
        }
    }

    /// <summary>Tests that canceling the ReadAsync call on the pipe reader created from the enumerable correctly
    /// cancels the enumerable iteration.</summary>
    [Test]
    public async Task Enumerable_pipe_reader_read_async_cancellation_cancels_enumerator()
    {
        // Arrange
        var canceledTcs = new TaskCompletionSource();
        var payload = GetDataAsync(default).ToPipeReader();

        using var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readResultTask = payload.ReadAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(
            async () => await readResultTask,
            Throws.InstanceOf<OperationCanceledException>().With.Property("CancellationToken").EqualTo(cts.Token));
        await canceledTcs.Task;

        // Cleanup
        payload.Complete();

        async IAsyncEnumerable<InputMessage> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(-1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                canceledTcs.SetResult();
                throw;
            }

            yield return new InputMessage
            {
                P1 = $"message-1",
                P2 = 2,
            };
        }
    }

    /// <summary>Tests that stopping the full enumerable correctly completes the pipe reader.</summary>
    [Test]
    public async Task Partial_enumeration_completes_the_pipe_reader()
    {
        // Arrange
        var pipe = new Pipe();
        await EncodeDataAsync(pipe.Writer);

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        IAsyncEnumerable<InputMessage> values = payload.ToAsyncEnumerable(InputMessage.Parser, 16 * 1024, default);

        // Act
        int count = 0;
        await foreach (var value in values)
        {
            count++;
            if (value.P2 == 1)
            {
                break;
            }
        }

        // Assert
        Assert.That(count, Is.EqualTo(2)); // read 2 elements
        Assert.That(() => payload.Completed, Is.Null);
        Assert.That(async () => (await pipe.Writer.FlushAsync()).IsCompleted, Is.True);

        // Cleanup
        pipe.Writer.Complete();

        static async Task EncodeDataAsync(PipeWriter writer)
        {
            for (int i = 0; i < 10; i++)
            {
                var inputMessage = new InputMessage
                {
                    P1 = $"message-{i}",
                    P2 = i
                };
                var payload = inputMessage.EncodeAsLengthPrefixedMessage(new PipeOptions());
                await payload.CopyToAsync(writer);
            }
            await writer.FlushAsync();
        }
    }

#pragma warning disable CA1001 // _listener is disposed by Listen caller.
    private sealed class TestAsyncEnumerable : IAsyncEnumerable<InputMessage>
#pragma warning restore CA1001
    {
        internal TestAsyncEnumerator Enumerator =>
            _enumerator ??
            throw new InvalidOperationException("Call GetAsyncEnumerator first");

        private TestAsyncEnumerator? _enumerator;

        public IAsyncEnumerator<InputMessage> GetAsyncEnumerator(CancellationToken cancellationToken) =>
            _enumerator = new TestAsyncEnumerator();
    }

    private sealed class TestAsyncEnumerator : IAsyncEnumerator<InputMessage>
    {
        public InputMessage Current { get; private set; }

        internal Task DisposeCalled => _disposeCalled.Task;

        private readonly TaskCompletionSource _disposeCalled = new();

        public TestAsyncEnumerator()
        {
            Current = new InputMessage
            {
                P1 = $"message-1",
                P2 = 1
            };
        }

        public ValueTask DisposeAsync()
        {
            _disposeCalled.TrySetResult();
            return default;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            Current = new InputMessage
            {
                P1 = $"message-{Current.P2 + 1}",
                P2 = Current.P2 + 1
            };
            return new(true);
        }
    }
}
