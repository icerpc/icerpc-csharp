// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Timeout(5000)]
public class StreamTests
{
    /// <summary>Verifies that we correctly encode an async enumerable of fixed size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_fixed_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => i).ToArray();

        // Act
        PipeReader payload = new ServicePrx().CreatePayloadStream(
            GetDataAsync(size),
            SliceEncoding.Slice2,
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

    /// <summary>Verifies that we can create a payload stream over an async enumerable of variable size elements.
    /// </summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_variable_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();

        // Act
        PipeReader payload = new ServicePrx().CreatePayloadStream(
            GetDataAsync(size),
            SliceEncoding.Slice2,
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
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Decode_stream_of_fixed_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var pipe = new Pipe();
        var request = new IncomingRequest(InvalidConnection.IceRpc)
        {
            Payload = pipe.Reader
        };
        int[] expected = Enumerable.Range(0, size).Select(i => i).ToArray();
        Task.Run(() => _ = EncodeDataAsync(pipe.Writer));

        // Act
        IAsyncEnumerable<int> decoded = request.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            SliceDecodeOptions.Default,
            defaultActivator: null,
            defaultInvoker: Proxy.DefaultInvoker,
            prxEncodeOptions: null,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        // Assert
        Assert.That(async () => await ToArrayAsync(decoded), Is.EqualTo(expected));

        async Task<int[]> ToArrayAsync(IAsyncEnumerable<int> enumerable)
        {
            var data = new List<int>();
            await foreach (int i in enumerable)
            {
                data.Add(i);
            }
            return data.ToArray();
        }

        async Task EncodeDataAsync(PipeWriter writer)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await writer.FlushAsync();
                    await Task.Yield();
                }
                EncodeElement(i);
            }
            await writer.CompleteAsync();

            void EncodeElement(int value)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
                encoder.EncodeInt32(value);
            }
        }
    }

    /// <summary>Verifies that we correctly decode an async enumerable of variable size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Decode_stream_of_variable_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var pipe = new Pipe();
        var request = new IncomingRequest(InvalidConnection.IceRpc)
        {
            Payload = pipe.Reader
        };
        string[] expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();
        Task.Run(() => _ = EncodeDataAsync(pipe.Writer));

        // Act
        IAsyncEnumerable<string> decoded = request.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            SliceDecodeOptions.Default,
            defaultActivator: null,
            defaultInvoker: Proxy.DefaultInvoker,
            prxEncodeOptions: null,
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(async () => await ToArrayAsync(decoded), Is.EqualTo(expected));

        async Task<string[]> ToArrayAsync(IAsyncEnumerable<string> enumerable)
        {
            var data = new List<string>();
            await foreach (string i in enumerable)
            {
                data.Add(i);
            }
            return data.ToArray();
        }

        async Task EncodeDataAsync(PipeWriter writer)
        {
            int encodedByteCount = 0;
            Memory<byte> sizePlaceHolder = writer.GetMemory(4)[0..4];
            writer.Advance(4);
            for (int i = 0; i < size; i++)
            {
                if (encodedByteCount > 0 && yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    SliceEncoder.EncodeVarUInt62((ulong)encodedByteCount, sizePlaceHolder.Span);
                    encodedByteCount = 0;
                    await writer.FlushAsync();
                    await Task.Yield();
                    sizePlaceHolder = writer.GetMemory(4)[0..4];
                    writer.Advance(4);
                }
                encodedByteCount += EncodeElement($"hello-{i}");
            }
            if (encodedByteCount > 0)
            {
                SliceEncoder.EncodeVarUInt62((ulong)encodedByteCount, sizePlaceHolder.Span);
            }
            await writer.CompleteAsync();

            int EncodeElement(string value)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
                encoder.EncodeString(value);
                return encoder.EncodedByteCount;
            }
        }
    }
}
