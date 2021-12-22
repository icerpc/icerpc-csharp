// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.SliceInternal
{
    [Parallelizable(ParallelScope.All)]
    public sealed class PayloadPipeReaderTests
    {
        [TestCase(16)]
        [TestCase(128)]
        [TestCase(256)]
        [TestCase(384)]
        [TestCase(512)]
        public void PayloadPipeReader_WriteAndRead_OneSegment(int size)
        {
            var payloadReader = new PayloadPipeReader();
            IBufferWriter<byte> payloadWriter = payloadReader;
            Memory<byte> buffer = payloadWriter.GetMemory(size)[0..size];
            new Random().NextBytes(buffer.Span);
            payloadWriter.Advance(size);

            Assert.That(payloadReader.TryRead(out ReadResult result), Is.True);
            Assert.That(result.IsCompleted);
            Assert.That(result.Buffer.Length, Is.EqualTo(size));
            Assert.That(buffer.Span.SequenceEqual(result.Buffer.ToArray()), Is.True);
        }

        [TestCase(16, 16)]
        [TestCase(128, 128)]
        [TestCase(128, 256)]
        [TestCase(128, 1024)]
        [TestCase(256, 256)]
        [TestCase(256, 384)]
        [TestCase(256, 512)]
        [TestCase(256, 768)]
        [TestCase(256, 1024)]
        public void PayloadPipeReader_WriteAndRead_MultipleSegments(int size1, int size2)
        {
            var payloadReader = new PayloadPipeReader();
            IBufferWriter<byte> payloadWriter = payloadReader;

            Memory<byte> buffer1 = payloadWriter.GetMemory(size1)[0..size1];
            new Random().NextBytes(buffer1.Span);
            payloadWriter.Advance(size1);

            Memory<byte> buffer2 = payloadWriter.GetMemory(size2)[0..size2];
            new Random().NextBytes(buffer2.Span);
            payloadWriter.Advance(size2);

            Assert.That(payloadReader.TryRead(out ReadResult result), Is.True);
            Assert.That(result.IsCompleted);
            Assert.That(result.Buffer.Length, Is.EqualTo(size1 + size2));

            Span<byte> readBuffer = result.Buffer.ToArray().AsSpan();
            Assert.That(buffer1.Span.SequenceEqual(readBuffer[0..size1]), Is.True);
            Assert.That(buffer2.Span.SequenceEqual(readBuffer[size1..]), Is.True);
        }
    }
}
