// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

public class StreamTests
{
    /// <summary>Verifies that we correctly encode an async enumerable of fixed size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload continuation pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_fixed_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => i).ToArray();

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

    /// <summary>Verifies that we can create a payload continuation over an async enumerable of variable size elements.
    /// </summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload continuation pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_variable_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();

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

        // Act
        IAsyncEnumerable<int> decoded = pipe.Reader.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            SliceFeature.Default);

        // Assert
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

        // Act
        IAsyncEnumerable<string> decoded = pipe.Reader.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
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

    /// <summary>Test that the payload of an incoming request is completed successfully after the async enumerable
    /// decoding action throws <see cref="InvalidDataException" />.</summary>
    [Test]
    public async Task Decode_stream_of_variable_size_elements_containing_invalid_data_completes_payload()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeSegment(pipe.Writer);
        await pipe.Writer.FlushAsync();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        // Act
        IAsyncEnumerable<MyEnum> values = payload.ToAsyncEnumerable<MyEnum>(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => throw new InvalidDataException("invalid data"));

        // Assert
        Assert.That(async () => await values.GetAsyncEnumerator().MoveNextAsync(), Throws.TypeOf<InvalidDataException>());

        // The call to ToAsyncEnumerable does not decode any element synchronously, so we must await Completed _after_
        // the iteration above.
        Assert.That(async () => await payload.Completed, Throws.Nothing);
        pipe.Writer.Complete();

        static void EncodeSegment(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeSize(4);
            encoder.EncodeInt32(10);
        }
    }

    /// <summary>Test that the payload of an incoming request is completed successfully after the async enumerable
    /// decoding action throws <see cref="InvalidDataException" />.</summary>
    [Test]
    public async Task Decode_stream_of_fixed_size_elements_containing_invalid_data_completes_payload()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeData(pipe.Writer);
        await pipe.Writer.FlushAsync();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        // Act
        IAsyncEnumerable<MyEnum> values = payload.ToAsyncEnumerable<MyEnum>(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => throw new InvalidDataException("invalid data"),
            elementSize: 4,
            sliceFeature: null);

        // Assert
        Assert.That(async () => await values.GetAsyncEnumerator().MoveNextAsync(), Throws.TypeOf<InvalidDataException>());
        Assert.That(async () => await payload.Completed, Throws.Nothing);
        pipe.Writer.Complete();

        static void EncodeData(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeInt32(10);
        }
    }

    /// <summary>Tests the decoding of a stream where the recipient cancels the iteration.</summary>
    [Test]
    public async Task Decode_stream_with_cancellation()
    {
        // Arrange
        var pipe = new Pipe();
        EncodeData(pipe.Writer);
        _ = await pipe.Writer.FlushAsync();

        var payload = new PayloadPipeReaderDecorator(pipe.Reader);

        using var cts = new CancellationTokenSource();
        CancellationToken cancel = cts.Token;
        int count = 0;

        IAsyncEnumerable<int> values = payload.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4,
            sliceFeature: null);

        // Act
        await foreach (int value in values.WithCancellation(cancel))
        {
            count++;
            if (value == 20)
            {
                cts.Cancel();
            }
        }

        // Assert
        Assert.That(count, Is.EqualTo(2)); // read 2 elements
        Assert.That(() => payload.Completed, Throws.Nothing);
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
        Assert.That(() => payload.Completed, Throws.Nothing);

        static void EncodeData(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeInt32(10);
            encoder.EncodeInt32(20);
            encoder.EncodeInt32(30);
            encoder.EncodeInt32(40);
        }
    }
}
