// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class BufferedReceiverTests
    {
        [Test]
        public void BufferedReceiver_Dispose()
        {
            var receiver = new BufferedReceiver((Memory<byte> buffer, CancellationToken cancel) => default, 256);
            receiver.Dispose();
            receiver.Dispose();
        }

        [TestCase(10, 5, 100)]
        [TestCase(100, 100, 100)]
        [TestCase(100, 200, 100)]
        [TestCase(200, 5, 100)]
        [TestCase(200, 100, 100)]
        [TestCase(200, 150, 100)]
        [TestCase(200, 200, 100)]
        public async ValueTask BufferedReceiver_ReceiveAsync(int userBufferSize, int receiveSize, int bufferSize)
        {
            byte[] sourceBuffer = new byte[userBufferSize * 100];
            new Random().NextBytes(sourceBuffer);

            using var receiver = new BufferedReceiver(GetReceiver(sourceBuffer, receiveSize), bufferSize);

            ReadOnlyMemory<byte> compareBuffer = sourceBuffer;
            for (int i = 0; i < 100; ++i)
            {
                Memory<byte> buffer = new byte[userBufferSize];
                await receiver.ReceiveAsync(buffer, default);
                Assert.That(buffer.ToArray(), Is.EqualTo(compareBuffer[0..userBufferSize].ToArray()));
                compareBuffer = compareBuffer[userBufferSize..];
            }
        }

        [TestCase(5, 100)]
        [TestCase(100, 100)]
        [TestCase(200, 100)]
        [TestCase(5, 100)]
        [TestCase(100, 100)]
        [TestCase(150, 100)]
        [TestCase(200, 100)]
        public async ValueTask BufferedReceiver_ReceiveByteAsync(int receiveSize, int bufferSize)
        {
            byte[] sourceBuffer = new byte[1024];
            new Random().NextBytes(sourceBuffer);

            using var receiver = new BufferedReceiver(GetReceiver(sourceBuffer, receiveSize), bufferSize);

            for (int i = 0; i < sourceBuffer.Length; ++i)
            {
                Assert.That(await receiver.ReceiveByteAsync(default), Is.EqualTo(sourceBuffer[i]));
            }
        }

        [TestCase(5, 100)]
        [TestCase(100, 100)]
        [TestCase(200, 100)]
        [TestCase(5, 100)]
        [TestCase(100, 100)]
        [TestCase(150, 100)]
        [TestCase(200, 100)]
        public async ValueTask BufferedReceiver_ReceiveSizeAsync(int receiveSize, int bufferSize)
        {
            Memory<byte> sourceBuffer = new byte[4096];
            var bufferWriter = new SingleBufferWriter(sourceBuffer);
            var values = new List<int>();

            Encode(bufferWriter, values);

            sourceBuffer = bufferWriter.WrittenBuffer;

            using var receiver = new BufferedReceiver(GetReceiver(sourceBuffer, receiveSize), bufferSize);

            foreach (int value in values)
            {
                Assert.That(await receiver.ReceiveSizeAsync(default), Is.EqualTo(value));
            }

            static void Encode(IBufferWriter<byte> bufferWriter, List<int> values)
            {
                var encoder = new IceEncoder(bufferWriter, Encoding.Ice20);

                encoder.EncodeSize(0);
                values.Add(0);
                for (int i = 0; i < 31; ++i)
                {
                    encoder.EncodeSize(1 << i);
                    values.Add(1 << i);
                }
            }
        }

        [TestCase(5, 100)]
        [TestCase(100, 100)]
        [TestCase(200, 100)]
        [TestCase(5, 100)]
        [TestCase(100, 100)]
        [TestCase(150, 100)]
        [TestCase(200, 100)]
        public async ValueTask BufferedReceiver_ReceiveVarULongAsync(int receiveSize, int bufferSize)
        {
            Memory<byte> sourceBuffer = new byte[4096];
            var bufferWriter = new SingleBufferWriter(sourceBuffer);
            var values = new List<ulong>();
            Encode(bufferWriter, values);

            sourceBuffer = bufferWriter.WrittenBuffer;

            using var receiver = new BufferedReceiver(GetReceiver(sourceBuffer, receiveSize), bufferSize);

            foreach (ulong value in values)
            {
                Assert.That((await receiver.ReceiveVarULongAsync(default)).Value, Is.EqualTo(value));
            }

            static void Encode(IBufferWriter<byte> bufferWriter, List<ulong> values)
            {
                var encoder = new IceEncoder(bufferWriter, Encoding.Ice20);

                encoder.EncodeSize(0);
                values.Add(0);
                for (int i = 0; i < 62; ++i)
                {
                    encoder.EncodeVarULong((ulong)1 << i);
                    values.Add((ulong)1 << i);
                }
            }
        }

        private static Func<Memory<byte>, CancellationToken, ValueTask<int>> GetReceiver(
            ReadOnlyMemory<byte> sourceBuffer,
            int receiveSize) =>
            (Memory<byte> buffer, CancellationToken cancel) =>
                {
                    // Copy receiveSize bytes to the user buffer.
                    int size = Math.Min(receiveSize, Math.Min(buffer.Length, sourceBuffer.Length));
                    sourceBuffer[0..size].CopyTo(buffer);
                    sourceBuffer = sourceBuffer[size..];
                    return new(size);
                };
    }
}
