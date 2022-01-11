// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using NUnit.Framework;

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

        // [TestCase(1, 1)]
        // [TestCase(1, 10)]
        // [TestCase(10, 10)]
        // public void CircularBuffer_Enqueue(int size, int capacity)
        // {
        //     var buffer = new CircularBuffer(capacity);
        //     Memory<byte> b = buffer.GetWriteBuffer(size);
        //     Assert.AreEqual(size, b.Length);
        // }

        // [TestCase(0, 1)]
        // [TestCase(10, 1)]
        // public void CircularBuffer_Enqueue_Exception(int size, int capacity)
        // {
        //     var buffer = new CircularBuffer(capacity);
        //     Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetWriteBuffer(size));
        // }

        // [TestCase(1, 1)]
        // [TestCase(1, 10)]
        // [TestCase(10, 10)]
        // public void CircularBuffer_Consume(int size, int capacity)
        // {
        //     var buffer = new CircularBuffer(capacity);
        //     _ = buffer.GetWriteBuffer(size);
        //     Memory<byte> c = new byte[size];
        //     buffer.Consume(c);
        // }

        // [TestCase(0, 1)]
        // [TestCase(10, 1)]
        // public void CircularBuffer_Consume_Exception(int size, int capacity)
        // {
        //     var buffer = new CircularBuffer(capacity);
        //     Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Consume(new byte[size]));
        // }

        // [TestCase(1, 1)]
        // [TestCase(1, 10)]
        // [TestCase(6, 10)]
        // [TestCase(10, 10)]
        // [TestCase(10, 100)]
        // [TestCase(40, 100)]
        // public void CircularBuffer_EnqueueAndConsume(int size, int capacity)
        // {
        //     var buffer = new CircularBuffer(capacity);
        //     Memory<byte> p = Fill(buffer.GetWriteBuffer(size));
        //     Memory<byte> c = new byte[size];
        //     buffer.Consume(c);
        //     Assert.AreEqual(p.ToArray(), c.ToArray());

        //     for (int sz = 1; sz < size + 1; ++sz)
        //     {
        //         c = new byte[sz];
        //         for (int i = 0; i < 2 * capacity; ++i)
        //         {
        //             p = Fill(buffer.GetWriteBuffer(sz));
        //             if (p.Length < sz)
        //             {
        //                 // The Enqueue request couldn't be satisfied with a single memory block if the returned
        //                 // memory block is smaller than the requested size. In this case, we make another Enqueue
        //                 // request for the remaining size.
        //                 Memory<byte> p2 = Fill(buffer.GetWriteBuffer(sz - p.Length), p.Length);
        //                 buffer.Consume(c);
        //                 Assert.AreEqual(p.ToArray(), c.Slice(0, p.Length).ToArray());
        //                 Assert.AreEqual(p2.ToArray(), c.Slice(p.Length, p2.Length).ToArray());
        //             }
        //             else
        //             {
        //                 buffer.Consume(c);
        //                 Assert.AreEqual(p.ToArray(), c.ToArray());
        //             }
        //         }
        //     }

        //     static Memory<byte> Fill(Memory<byte> memory, int start = 0)
        //     {
        //         for (int i = 0; i < memory.Span.Length; ++i)
        //         {
        //             memory.Span[i] = (byte)(start + i);
        //         }
        //         return memory;
        //     }
        // }
    }
}
