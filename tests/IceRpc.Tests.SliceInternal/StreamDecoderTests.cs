// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.SliceInternal
{
    [Parallelizable(ParallelScope.All)]
    [Timeout(5000)]
    public sealed class StreamDecoderTests
    {
        private static IEnumerable<int> DecodeBufferIntoInts(ReadOnlySequence<byte> buffer)
        {
            Assert.That(buffer.Length % 4 == 0);
            var decoder = new IceDecoder(buffer, Encoding.Ice20);
            var items = new int[buffer.Length / 4];
            for (int i = 0; i < items.Length; ++i)
            {
                items[i] = decoder.DecodeInt();
            }
            return items;
        }

        private static ReadOnlySequence<byte> CreateBuffer(int value, int count)
        {
            var bufferWriter = new SingleBufferWriter(new byte[count * 4]);
            var encoder = new IceEncoder(bufferWriter, Encoding.Ice20);
            for (int i = 0; i < count; ++i)
            {
                encoder.EncodeInt(value);
            }
            Assert.That(bufferWriter.WrittenBuffer.Length == count * 4);
            return new ReadOnlySequence<byte>(bufferWriter.WrittenBuffer);
        }

        [Test]
        public void StreamDecoder_Options()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StreamDecoderOptions(pauseWriterThreshold: -2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new StreamDecoderOptions(pauseWriterThreshold: 100, resumeWriterThreshold: 200));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new StreamDecoderOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 200));

            Assert.DoesNotThrow(() => new StreamDecoderOptions(pauseWriterThreshold: -1, resumeWriterThreshold: 200));
            Assert.DoesNotThrow(() => new StreamDecoderOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0));
        }

        [Test]
        public async Task StreamDecoder_SlowReader()
        {
            var streamDecoder = new StreamDecoder<int>(
                DecodeBufferIntoInts,
                new StreamDecoderOptions(pauseWriterThreshold: 500)); // 500 bytes, 125 ints

            var buffer = CreateBuffer(value: 123, count: 100);
            Assert.That(await streamDecoder.WriteAsync(buffer, default), Is.False); // 100 ints, 400 bytes
            Assert.That(await streamDecoder.WriteAsync(buffer, default), Is.False); // 200 ints, 800 bytes total

            Task<bool> task = streamDecoder.WriteAsync(buffer, default).AsTask();   // when done, 300 ints total
            Assert.That(task.IsCompleted, Is.False);

            IAsyncEnumerator<int> asyncEnumerator = streamDecoder.ReadAsync(default).GetAsyncEnumerator();

            await ReadAsync(100); // 100 ints / 400 bytes remaining, still paused
            Assert.That(task.IsCompleted, Is.False);
            // TODO: how to reliably verify the writer is still paused?

            // consuming 1 extra item "consumes" 100 ints / 400 bytes as far as the pause/resume logic is concerned
            await ReadAsync(1);
            Assert.That(await task, Is.False);

            streamDecoder.CompleteWriter();
            Assert.That(await ReadAsync(199), Is.True);
            Assert.That(await ReadAsync(1), Is.False);

            async Task<bool> ReadAsync(int count)
            {
                do
                {
                    bool found = await asyncEnumerator.MoveNextAsync();
                    if (found)
                    {
                        Assert.That(asyncEnumerator.Current, Is.EqualTo(123));
                    }
                    else
                    {
                        return false; // end of iteration, i.e. writer completed
                    }
                }
                while (--count > 0);
                return true;
            }
        }

        [Test]
        public async Task StreamDecoder_SlowWriter()
        {
            // The reader pauses because the queue if often empty.

            var streamDecoder = new StreamDecoder<int>(DecodeBufferIntoInts);

            IAsyncEnumerable<int> asyncEnumerable = streamDecoder.ReadAsync(default);
            IAsyncEnumerator<int> asyncEnumerator = asyncEnumerable.GetAsyncEnumerator();

            Task<bool> task;

            for (int i = 0; i < 10; ++i) // stop/start 10 times
            {
                task = asyncEnumerator.MoveNextAsync().AsTask();
                Assert.That(task.IsCompleted, Is.False);

                ReadOnlySequence<byte> buffer = CreateBuffer(value: 123, count: 4);
                await streamDecoder.WriteAsync(buffer, default); // writes 2 x 4
                await streamDecoder.WriteAsync(buffer, default);
                Assert.That(await task, Is.True); // reads 1
                Assert.That(asyncEnumerator.Current == 123);

                for (int j = 0; j < 7; ++j) // reads remaining 7
                {
                    task = asyncEnumerator.MoveNextAsync().AsTask();
                    Assert.That(task.IsCompleted, Is.True);
                    Assert.That(await task, Is.True);
                    Assert.That(asyncEnumerator.Current == 123);
                }
            }

            task = asyncEnumerator.MoveNextAsync().AsTask();
            Assert.That(task.IsCompleted, Is.False);
            streamDecoder.CompleteWriter();
            Assert.That(await task, Is.False);
        }

        [Test]
        public async Task StreamDecoder_WriterNoPause()
        {
            var streamDecoder = new StreamDecoder<int>(
                DecodeBufferIntoInts,
                new StreamDecoderOptions(pauseWriterThreshold: 0)); // no pause threshold

            // Write 20,000 ints (20,000 * 4 > 64K)

            var buffer = CreateBuffer(value: 456, count: 1000);
            for (int i = 0; i < 20; ++i)
            {
                await streamDecoder.WriteAsync(buffer, default); // if this blocks the test will hang since there is
            }                                                    // nobody reading

            streamDecoder.CompleteWriter();

            // Now read all 20,000 ints
            int count = 0;
            await foreach (int i in streamDecoder.ReadAsync(default))
            {
                Assert.That(i, Is.EqualTo(456));
                count++;
            }
            Assert.That(count, Is.EqualTo(20_000));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task StreamDecoder_CancelRead(bool callCancel)
        {
            var streamDecoder = new StreamDecoder<int>(DecodeBufferIntoInts);

            Task readerTask = Task.Run(async () =>
            {
                using var cancellationTokenSource = new CancellationTokenSource();
                CancellationToken cancel = cancellationTokenSource.Token;

                int count = 0;

                await foreach (int i in streamDecoder.ReadAsync(default).WithCancellation(cancel))
                {
                    count++;
                    Assert.That(i, Is.EqualTo(123));

                    if (callCancel && count == 150)
                    {
                        // stop writer
                        cancellationTokenSource.Cancel();
                    }

                    if (!callCancel && count == 180)
                    {
                        break;
                    }
                }

                // The cancellation triggers the immediate end of the async-iteration
                Assert.That(count, Is.EqualTo(callCancel ? 150 : 180));
            });

            var buffer = CreateBuffer(value: 123, count: 100);
            Assert.That(await streamDecoder.WriteAsync(buffer, default), Is.False);
            Assert.That(await streamDecoder.WriteAsync(buffer, default), Is.False);
            await readerTask;

            // The writer gets the "reader completed" notification only when we call cancel on the
            // cancellationTokenSource.
            Assert.That(await streamDecoder.WriteAsync(buffer, default), Is.EqualTo(callCancel));
            streamDecoder.CompleteWriter();
        }
    }
}
