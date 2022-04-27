// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

public class StreamTests
{
    /// <summary>Verifies that we can create a payload stream over an async enumerable.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Create_payload_stream(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => i).ToArray();

        // Act
        PipeReader payload = SliceEncoding.Slice2.CreatePayloadStream(
            GetDataAsync(size),
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));

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
                int size = decoder.DecodeSize() / sizeof(int);
                for (int i = 0; i < size; i++)
                {
                    data.Add(decoder.DecodeInt32());
                }
            }
            return data;
        }
    }
}
