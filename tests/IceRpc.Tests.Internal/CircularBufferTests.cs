// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using ZeroC.Ice;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class CircularBufferTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(1024)]
        public void CircularBuffer_Constructor(int capacity)
        {
            _ = new CircularBuffer(capacity);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void CircularBuffer_Constructor_Exception(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CircularBuffer(capacity));
        }

        [TestCase(1, 1)]
        [TestCase(1, 10)]
        [TestCase(10, 10)]
        public void CircularBuffer_Enqueue(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Memory<byte> b = buffer.Enqueue(size);
            Assert.AreEqual(b.Length, size);
        }

        [TestCase(0, 1)]
        [TestCase(10, 1)]
        public void CircularBuffer_Enqueue_Exception(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Enqueue(size));
        }

        [TestCase(1, 1)]
        [TestCase(1, 10)]
        [TestCase(10, 10)]
        public void CircularBuffer_Consume(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Memory<byte> p = buffer.Enqueue(size);
            Memory<byte> c = new byte[size];
            buffer.Consume(c);
        }

        [TestCase(0, 1)]
        [TestCase(10, 1)]
        public void CircularBuffer_Consume_Exception(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Consume(new byte[size]));
        }

        [TestCase(1, 1)]
        [TestCase(1, 10)]
        [TestCase(10, 10)]
        public void CircularBuffer_EnqueueAndConsume(int size, int capacity)
        {
            var buffer = new CircularBuffer(capacity);
            Memory<byte> p = buffer.Enqueue(size);
            for (int i = 0; i < size; ++i)
            {
                p.Span[i] = (byte)i;
            }
            Memory<byte> c = new byte[size];
            buffer.Consume(c);
            Assert.AreEqual(p.ToArray(), c.ToArray());

            c = new byte[1];
            for (int i = 0; i < size; ++i)
            {
                buffer.Enqueue(1).Span[0] = (byte)i;
                buffer.Consume(c);
                Assert.AreEqual(c.Span[0], (byte)i);
            }
        }
    }
}
