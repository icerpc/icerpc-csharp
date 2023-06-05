// Copyright (c) ZeroC, Inc.

using IceRpc.Internal; // For InvalidPipeReader
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Tests.Slice;

public class StreamTests
{
    /// <summary>Verifies that we correctly encode an async enumerable of fixed size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_fixed_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        int[] expected = Enumerable.Range(0, size).Select(i => i).ToArray();

        // Act
        var payload = GetDataAsync(size).ToPipeReader(
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

        // Assert
        Assert.That(async () => await DecodeDataAsync(payload), Is.EqualTo(expected));

        async IAsyncEnumerable<int> GetDataAsync(int size)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await Task.Yield();
                }
                yield return i;
            }
        }

        async Task<int[]> DecodeDataAsync(PipeReader payload)
        {
            var data = new List<int>();
            ReadResult readResult;
            do
            {
                readResult = await payload.ReadAsync();
                data.AddRange(DecodeIntStream(readResult.Buffer));
                payload.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return data.ToArray();
        }

        static List<int> DecodeIntStream(ReadOnlySequence<byte> buffer)
        {
            var data = new List<int>();
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            if (buffer.Length > 0)
            {
                while (decoder.Consumed < buffer.Length)
                {
                    data.Add(decoder.DecodeInt32());
                }
            }
            return data;
        }
    }

    /// <summary>Verifies that we correctly encode an async enumerable of variable size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_variable_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        string[] expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();

        // Act
        var payload = GetDataAsync(size).ToPipeReader(
            (ref SliceEncoder encoder, string value) => encoder.EncodeString(value),
            useSegments: true);

        // Assert
        Assert.That(async () => await DecodeDataAsync(payload), Is.EqualTo(expected));

        async IAsyncEnumerable<string> GetDataAsync(int size)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await Task.Yield();
                }
                yield return $"hello-{i}";
            }
        }

        async Task<string[]> DecodeDataAsync(PipeReader payload)
        {
            var data = new List<string>();
            ReadResult readResult;
            do
            {
                readResult = await payload.ReadSegmentAsync(SliceEncoding.Slice2, int.MaxValue - 1, default);
                data.AddRange(DecodeStringStream(readResult.Buffer));
                payload.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return data.ToArray();
        }

        static List<string> DecodeStringStream(ReadOnlySequence<byte> buffer)
        {
            var data = new List<string>();
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            if (buffer.Length > 0)
            {
                while (decoder.Consumed < buffer.Length)
                {
                    data.Add(decoder.DecodeString());
                }
            }
            return data;
        }
    }

    /// <summary>Verifies that we correctly decode an async enumerable of fixed size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    [Test]
    public void Decode_stream_of_fixed_size_elements([Values(0, 100, 64 * 1024)] int size)
    {
        // Arrange
        var pipe = new Pipe();

        int[] expected = Enumerable.Range(0, size).Select(i => i).ToArray();
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        for (int i = 0; i < size; i++)
        {
            encoder.EncodeInt32(i);
        }
        pipe.Writer.Complete();

        IAsyncEnumerable<int> decoded = pipe.Reader.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            SliceFeature.Default);

        // Act/Assert
        Assert.That(async () => await ToArrayAsync(decoded), Is.EqualTo(expected));

        static async Task<int[]> ToArrayAsync(IAsyncEnumerable<int> enumerable)
        {
            var data = new List<int>();
            await foreach (int i in enumerable)
            {
                data.Add(i);
            }
            return data.ToArray();
        }
    }

    /// <summary>Verifies that we correctly decode an async enumerable of variable size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    [Test]
    public void Decode_stream_of_variable_size_elements([Values(0, 100, 64 * 1024)] int size)
    {
        // Arrange
        var pipe = new Pipe();
        string[] expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();

        if (size > 0)
        {
            // We encode the elements in 2 segments
            EncodeSegment(0, size / 2);
            EncodeSegment(size / 2, size);

            void EncodeSegment(int start, int end)
            {
                Memory<byte> sizePlaceHolder = pipe.Writer.GetMemory(4)[0..4];
                pipe.Writer.Advance(4);

                var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
                for (int i = start; i < end; i++)
                {
                    encoder.EncodeString($"hello-{i}");
                }
                SliceEncoder.EncodeVarUInt62((ulong)encoder.EncodedByteCount, sizePlaceHolder.Span);
            }
        }
        pipe.Writer.Complete();

        IAsyncEnumerable<string> decoded = pipe.Reader.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Act/Assert
        Assert.That(async () => await ToArrayAsync(decoded), Is.EqualTo(expected));

        static async Task<string[]> ToArrayAsync(IAsyncEnumerable<string> enumerable)
        {
            var data = new List<string>();
            await foreach (string i in enumerable)
            {
                data.Add(i);
            }
            return data.ToArray();
        }
    }

    /// <summary>Test that the payload is completed successfully after the async enumerable decoding action throws <see
    /// cref="InvalidDataException" />.</summary>
    [Test]
    public async Task Decode_stream_of_fixed_size_elements_containing_invalid_data_completes_payload()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeData(pipe.Writer);
        await pipe.Writer.FlushAsync();
        pipe.Writer.Complete();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        IAsyncEnumerable<MyEnum> values = payload.ToAsyncEnumerable<MyEnum>(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => throw new InvalidDataException("invalid data"),
            elementSize: 4,
            sliceFeature: null);
        await using IAsyncEnumerator<MyEnum> enumerator = values.GetAsyncEnumerator();

        // Act/Assert
        Assert.That(enumerator.MoveNextAsync, Throws.InstanceOf<InvalidDataException>());
        Assert.That(async () => await payload.Completed, Is.Null);

        static void EncodeData(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeInt32(10);
        }
    }

    /// <summary>Test that the payload is completed successfully after the async enumerable decoding action throws <see
    /// cref="InvalidDataException" />.</summary>
    [Test]
    public async Task Decode_stream_of_variable_size_elements_containing_invalid_data_completes_payload()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeSegment(pipe.Writer);
        await pipe.Writer.FlushAsync();
        pipe.Writer.Complete();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        IAsyncEnumerable<MyEnum> values = payload.ToAsyncEnumerable<MyEnum>(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => throw new InvalidDataException("invalid data"));
        await using IAsyncEnumerator<MyEnum> enumerator = values.GetAsyncEnumerator();

        // Act/Assert
        Assert.That(enumerator.MoveNextAsync, Throws.InstanceOf<InvalidDataException>());
        Assert.That(async () => await payload.Completed, Is.Null);

        static void EncodeSegment(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeSize(4);
            encoder.EncodeInt32(10);
        }
    }

    /// <summary>Tests the decoding of a stream where the sender sends elements in multiple chunks and as a result the
    /// decoding is asynchronous.</summary>
    [Test]
    public async Task Decode_stream_in_multiple_chunks()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeData(pipe.Writer);
        _ = await pipe.Writer.FlushAsync();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);
        int count = 0;

        IAsyncEnumerable<int> values = payload.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            sliceFeature: null);

        // Act
        await foreach (int value in values)
        {
            count++;
            if (value == 40) // last value of a chunk
            {
                if (count < 32) // 32 = 4 * 8
                {
                    // Encodes 4 additional elements
                    EncodeData(pipe.Writer);
                    _ = await pipe.Writer.FlushAsync();
                }
                else
                {
                    // Complete writer which means end iteration
                    pipe.Writer.Complete();
                }
            }
        }

        // Assert
        Assert.That(count, Is.EqualTo(32));
        Assert.That(() => payload.Completed, Is.Null);

        static void EncodeData(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeInt32(10);
            encoder.EncodeInt32(20);
            encoder.EncodeInt32(30);
            encoder.EncodeInt32(40);
        }
    }

    /// <summary>Test that abandoning the iteration completes the pipe reader from which the stream elements are being
    /// decoded.</summary>
    [Test]
    public async Task Decoding_completes_when_iteration_is_canceled()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeData(pipe.Writer);
        _ = await pipe.Writer.FlushAsync();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);
        int count = 0;

        IAsyncEnumerable<int> values = payload.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            sliceFeature: null);

        // Act
        await foreach (int value in values)
        {
            count++;
            if (value == 20)
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

        static void EncodeData(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeInt32(10);
            encoder.EncodeInt32(20);
            encoder.EncodeInt32(30);
            encoder.EncodeInt32(40);
        }
    }

    /// <summary>Tests that canceling the iteration while the decode function is waiting to read data, cancels
    /// the read operation and completes the enumerable and pipe reader.</summary>
    [Test]
    public async Task Decoding_completes_when_enumerator_read_is_canceled()
    {
        // Arrange
        var payload = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        payload.HoldRead = true;

        using var cts = new CancellationTokenSource();

        IAsyncEnumerable<int> values = payload.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            sliceFeature: null);

        await using var enumerator = values.WithCancellation(cts.Token).GetAsyncEnumerator();

        var moveNextAwaitable = enumerator.MoveNextAsync();
        await payload.ReadCalled;

        // Act
        cts.Cancel();

        // Assert
        Assert.That(payload.IsReadCanceled, Is.True);
        Assert.That(() => payload.Completed, Is.Null);
    }

    /// <summary>Tests that stopping the full enumerable correctly completes the pipe reader.</summary>
    [Test]
    public async Task Partial_enumeration_completes_the_pipe_reader()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeData(pipe.Writer);
        _ = await pipe.Writer.FlushAsync();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        IAsyncEnumerable<int> values = payload.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            sliceFeature: null);

        // Act
        int count = 0;
        await foreach (int value in values)
        {
            count++;
            if (value == 20)
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

        static void EncodeData(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeInt32(10);
            encoder.EncodeInt32(20);
            encoder.EncodeInt32(30);
            encoder.EncodeInt32(40);
        }
    }

    [Test]
    public async Task Enumerable_pipe_reader_completion_disposes_the_enumerator()
    {
        // Arrange
        var enumerable = new TestAsyncEnumerable();

        var payload = enumerable.ToPipeReader(
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

        // Act
        payload.Complete();

        // Assert
        await enumerable.Enumerator.DisposeCalled;
    }

    [Test]
    public async Task Enumerable_pipe_reader_read_fails_if_enumerable_throws_exception()
    {
        // Arrange
        var exception = new Exception();
        var payload = GetDataAsync().ToPipeReader(
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

        ReadResult readResult = await payload.ReadAsync();
        payload.AdvanceTo(readResult.Buffer.End);

        // Act/Assert
        Exception? throwException = Assert.ThrowsAsync<Exception>(async () => await payload.ReadAsync());
        Assert.That(throwException, Is.EqualTo(exception));

        // Cleanup
        payload.Complete();

        async IAsyncEnumerable<int> GetDataAsync()
        {
            yield return 10;
            await Task.Yield();
            throw exception;
        }
    }

    /// <summary>Tests that calling Complete on the pipe reader created from the enumerable correctly cancels the
    /// enumerable iteration.</summary>
    [Test]
    public async Task Enumerable_pipe_reader_completion_cancels_enumerator()
    {
        // Arrange
        var canceledTcs = new TaskCompletionSource();
        var payload = GetDataAsync(default).ToPipeReader(
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

        ReadResult readResult = await payload.ReadAsync();
        payload.AdvanceTo(readResult.Buffer.End);

        // Act
        payload.Complete();

        // Assert
        await canceledTcs.Task;

        async IAsyncEnumerable<int> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return 1;
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
        var payload = GetDataAsync(default).ToPipeReader(
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

        ValueTask<ReadResult> readResultTask = payload.ReadAsync();

        // Act
        payload.CancelPendingRead();

        // Assert
        ReadResult readResult = await readResultTask;
        Assert.That(readResult.IsCanceled, Is.True);
        await canceledTcs.Task;

        // Cleanup
        payload.Complete();

        async IAsyncEnumerable<int> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
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
            yield return 1;
        }
    }

    /// <summary>Tests that canceling the ReadAsync call on the pipe reader created from the enumerable correctly
    /// cancels the enumerable iteration.</summary>
    [Test]
    public async Task Enumerable_pipe_reader_read_async_cancellation_cancels_enumerator()
    {
        // Arrange
        var canceledTcs = new TaskCompletionSource();
        var payload = GetDataAsync(default).ToPipeReader(
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

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

        async IAsyncEnumerable<int> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
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
            yield return 1;
        }
    }

#pragma warning disable CA1001 // _listener is disposed by Listen caller.
    private sealed class TestAsyncEnumerable : IAsyncEnumerable<int>
#pragma warning restore CA1001
    {
        internal TestAsyncEnumerator Enumerator =>
            _enumerator ??
            throw new InvalidOperationException("Call GetAsyncEnumerator first");

        private TestAsyncEnumerator? _enumerator;

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken) =>
            _enumerator = new TestAsyncEnumerator();
    }

    private sealed class TestAsyncEnumerator : IAsyncEnumerator<int>
    {
        public int Current { get; private set; }

        internal Task DisposeCalled => _disposeCalled.Task;

        private readonly TaskCompletionSource _disposeCalled = new();

        public ValueTask DisposeAsync()
        {
            _disposeCalled.TrySetResult();
            return default;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            ++Current;
            return new(true);
        }
    }
}
