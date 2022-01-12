// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class CircularBufferTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(1024)]
        public void CircularBuffer_Constructor(int capacity) => _ = new CircularBuffer(capacity);

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void CircularBuffer_Constructor_Exception(int capacity) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CircularBuffer(capacity));

        [TestCase(1, 1)]
        [TestCase(1, 10)]
        [TestCase(10, 10)]
        public void CircularBuffer_GetWriteBuffer(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Memory<byte> b = buffer.GetWriteBuffer(size);
            Assert.AreEqual(size, b.Length);
        }

        [TestCase(0, 1)]
        [TestCase(10, 1)]
        public void CircularBuffer_GetWriteBuffer_Exception(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetWriteBuffer(size));
        }

        [Test]
        public void CircularBuffer_GetReadBuffer()
        {
            var buffer = new CircularBuffer(10);
            ReadOnlySequence<byte> sequence = buffer.GetReadBuffer();
            Assert.That(sequence.Length, Is.EqualTo(0));
        }

        [TestCase(1, 1)]
        [TestCase(1, 10)]
        [TestCase(10, 10)]
        public void CircularBuffer_Consume(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            _ = buffer.GetWriteBuffer(size);

            ReadOnlySequence<byte> sequence = buffer.GetReadBuffer();
            Assert.That(sequence.Length, Is.EqualTo(size));
            buffer.AdvanceTo(sequence.End);

            sequence = buffer.GetReadBuffer();
            Assert.That(sequence.Length, Is.EqualTo(0));
            buffer.AdvanceTo(sequence.End);
        }

        [TestCase(1, 1)]
        [TestCase(1, 10)]
        [TestCase(6, 10)]
        [TestCase(10, 10)]
        [TestCase(10, 100)]
        [TestCase(40, 100)]
        public void CircularBuffer_EnqueueAndConsume(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);

            Memory<byte> p = Fill(buffer.GetWriteBuffer(size));
            Memory<byte> c = new byte[size];
            Consume(ref buffer, c);
            Assert.AreEqual(p.ToArray(), c.ToArray());

            for (int sz = 1; sz < size + 1; ++sz)
            {
                c = new byte[sz];
                for (int i = 0; i < 2 * capacity; ++i)
                {
                    p = Fill(buffer.GetWriteBuffer(sz));
                    if (p.Length < sz)
                    {
                        // The Enqueue request couldn't be satisfied with a single memory block if the returned
                        // memory block is smaller than the requested size. In this case, we make another Enqueue
                        // request for the remaining size.
                        Memory<byte> p2 = Fill(buffer.GetWriteBuffer(sz - p.Length), p.Length);
                        Consume(ref buffer, c);
                        Assert.AreEqual(p.ToArray(), c[0..p.Length].ToArray());
                        Assert.AreEqual(p2.ToArray(), c.Slice(p.Length, p2.Length).ToArray());
                    }
                    else
                    {
                        Consume(ref buffer, c);
                        Assert.AreEqual(p.ToArray(), c.ToArray());
                    }
                }
            }

            static Memory<byte> Fill(Memory<byte> memory, int start = 0)
            {
                for (int i = 0; i < memory.Span.Length; ++i)
                {
                    memory.Span[i] = (byte)(start + i);
                }
                return memory;
            }

            static void Consume(ref CircularBuffer source, Memory<byte> destination)
            {
                ReadOnlySequence<byte> sequence = source.GetReadBuffer();
                if (sequence.Length > destination.Length)
                {
                    sequence = sequence.Slice(0, destination.Length);
                }

                SequencePosition position = sequence.Start;
                while (sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                {
                    memory.CopyTo(destination);
                    destination = destination[memory.Length..];
                }
                source.AdvanceTo(sequence.End);
            }
        }
    }
}
