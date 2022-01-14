// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace IceRpc.Tests.Slice.Stream
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    // [Log(LogAttributeLevel.Debug)]
    public sealed class StreamParamTests : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IStreamParamOperationsPrx _prx;
        private readonly ReadOnlySequence<byte> _sendBuffer;
        private readonly StreamParamOperations _servant;

        public StreamParamTests()
        {
            var buffer = new byte[256];
            new Random().NextBytes(buffer);
            _sendBuffer = new ReadOnlySequence<byte>(buffer);

            _servant = new StreamParamOperations(_sendBuffer);

            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => _servant)
                .BuildServiceProvider();
            _prx = StreamParamOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [TearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task StreamParam_Byte()
        {
            PipeReader reader;
            byte r1;
            int r2;

            reader = await _prx.OpStreamByteReceive0Async();
            ReadResult readResult = await reader.ReadAtLeastAsync(256);
            Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
            Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));

            (r1, reader) = await _prx.OpStreamByteReceive1Async();
            readResult = await reader.ReadAtLeastAsync(256);
            Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
            Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
            Assert.That(r1, Is.EqualTo(0x05));
            await reader.CompleteAsync();

            (r1, r2, reader) = await _prx.OpStreamByteReceive2Async();
            readResult = await reader.ReadAtLeastAsync(256);
            Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
            Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
            Assert.That(r1, Is.EqualTo(0x05));
            Assert.That(r2, Is.EqualTo(6));
            await reader.CompleteAsync();

            {
                var sendPipeReader = PipeReader.Create(_sendBuffer);
                await _prx.OpStreamByteSend0Async(sendPipeReader);
            }
            {
                var sendPipeReader = PipeReader.Create(_sendBuffer);
                await _prx.OpStreamByteSend1Async(0x08, sendPipeReader);
            }
            {
                var sendPipeReader = PipeReader.Create(_sendBuffer);
                await _prx.OpStreamByteSend2Async(0x08, 10, sendPipeReader);
            }

            {
                var sendPipeReader = PipeReader.Create(_sendBuffer);
                reader = await _prx.OpStreamByteSendReceive0Async(sendPipeReader);
                readResult = await reader.ReadAtLeastAsync(256);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
                Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
                await reader.CompleteAsync();
            }

            {
                var sendPipeReader = PipeReader.Create(_sendBuffer);
                (r1, reader) = await _prx.OpStreamByteSendReceive1Async(0x08, sendPipeReader);
                readResult = await reader.ReadAtLeastAsync(256);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
                Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
                Assert.That(r1, Is.EqualTo(0x08));
                await reader.CompleteAsync();
            }
            {
                var sendPipeReader = PipeReader.Create(_sendBuffer);
                (r1, r2, reader) = await _prx.OpStreamByteSendReceive2Async(
                    0x08,
                    10,
                    sendPipeReader);
                readResult = await reader.ReadAtLeastAsync(256);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
                Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
                Assert.That(r1, Is.EqualTo(0x08));
                Assert.That(r2, Is.EqualTo(10));
                await reader.CompleteAsync();
            }
        }

        [Test]
        public async Task StreamParam_Receive_MyStruct()
        {
            var v1 = new MyStruct(1, 1);
            var v2 = new MyStruct(2, 2);

            IAsyncEnumerable<MyStruct> stream = await _prx.OpStreamMyStructReceive0Async();
            _servant.EnumerableReceived.Release(1);
            var elements = new List<MyStruct>();
            await foreach (MyStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));

            MyStruct r1;
            (r1, stream) = await _prx.OpStreamMyStructReceive1Async();
            _servant.EnumerableReceived.Release(1);
            elements = new List<MyStruct>();
            await foreach (MyStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));

            MyStruct r2;
            (r1, r2, stream) = await _prx.OpStreamMyStructReceive2Async();
            _servant.EnumerableReceived.Release(1);
            elements = new List<MyStruct>();
            await foreach (MyStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));
            Assert.That(r2, Is.EqualTo(v2));
        }

        [Test]
        public async Task StreamParam_Send_MyStruct()
        {
            var v1 = new MyStruct(1, 1);
            var v2 = new MyStruct(2, 2);

            using var semaphore = new SemaphoreSlim(0);
            await _prx.OpStreamMyStructSend0Async(MyStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.MyStructs.Count, Is.EqualTo(0));
            // Release the semaphore to start streaming elements
            semaphore.Release(1);
            // Wait until the server received all elements
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.MyStructs.Count, Is.EqualTo(100));
            Assert.That(_servant.MyStructs.All(e => e == v1));

            _servant.MyStructs = ImmutableList<MyStruct>.Empty;
            await _prx.OpStreamMyStructSend1Async(v1, MyStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.MyStructs.Count, Is.EqualTo(0));
            // Release the semaphore to start streaming elements
            semaphore.Release(1);
            // Wait until the server received all elements
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.MyStructs.All(e => e == v1));

            _servant.MyStructs = ImmutableList<MyStruct>.Empty;
            await _prx.OpStreamMyStructSend2Async(v1, v2, MyStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.MyStructs.Count, Is.EqualTo(0));
            // Release the semaphore to start streaming elements
            semaphore.Release(1);
            // Wait until the server received all elements
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.MyStructs.Count, Is.EqualTo(100));
            Assert.That(_servant.MyStructs.All(e => e == v1));
        }

        [Test]
        public async Task StreamParam_Send_MyStructCancellation()
        {
            using var semaphore = new SemaphoreSlim(0);
            var canceled = new TaskCompletionSource<bool>();

            async IAsyncEnumerable<MyStruct> MyStructEnemerable0Async(
                [EnumeratorCancellation] CancellationToken cancel = default)
            {
                using CancellationTokenRegistration _ = cancel.Register(() => canceled.SetResult(true));
                for (int i = 0; i < 100; i++)
                {
                    await semaphore.WaitAsync(cancel);
                    yield return new MyStruct(1, 1);
                }
                canceled.SetResult(false);
            }

            await _prx.OpStreamMyStructSendAndCancel0Async(MyStructEnemerable0Async());
            // Start streaming data the server cancel its enumerable upon receive the first 20 items
            semaphore.Release(20);
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.MyStructs.Count, Is.EqualTo(20));
        }

        [Test]
        public async Task StreamParam_SendAndReceive_MyStruct()
        {
            using var semaphore = new SemaphoreSlim(0);

            var v1 = new MyStruct(1, 1);
            var v2 = new MyStruct(2, 2);

            IAsyncEnumerable<MyStruct> stream =
                await _prx.OpStreamMyStructSendReceive0Async(MyStructEnumerable(semaphore, 100, v1));
            semaphore.Release(1);
            var elements = new List<MyStruct>();
            await foreach (MyStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));

            MyStruct r1;
            (r1, stream) = await _prx.OpStreamMyStructSendReceive1Async(
                v1,
                MyStructEnumerable(semaphore, 100, v1));
            semaphore.Release(1);
            elements = new List<MyStruct>();
            await foreach (MyStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));

            MyStruct r2;
            (r1, r2, stream) = await _prx.OpStreamMyStructSendReceive2Async(
                v1,
                v2,
                MyStructEnumerable(semaphore, 100, v1));
            semaphore.Release(1);
            elements = new List<MyStruct>();
            await foreach (MyStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));
            Assert.That(r2, Is.EqualTo(v2));
        }

        [Test]
        // [Log(LogAttributeLevel.Debug)]
        public async Task StreamParam_Receive_AnotherStruct()
        {
            AnotherStruct v1 = GetAnotherStruct(1);
            AnotherStruct v2 = GetAnotherStruct(2);

            IAsyncEnumerable<AnotherStruct> stream = await _prx.OpStreamAnotherStructReceive0Async();
            _servant.EnumerableReceived.Release(1);
            var elements = new List<AnotherStruct>();
            await foreach (AnotherStruct item in stream)
            {
                elements.Add(item);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));

            AnotherStruct r1;
            (r1, stream) = await _prx.OpStreamAnotherStructReceive1Async();
            _servant.EnumerableReceived.Release(1);
            elements = new List<AnotherStruct>();
            await foreach (AnotherStruct item in stream)
            {
                elements.Add(item);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));

            AnotherStruct r2;
            (r1, r2, stream) = await _prx.OpStreamAnotherStructReceive2Async();
            _servant.EnumerableReceived.Release(1);
            elements = new List<AnotherStruct>();
            await foreach (AnotherStruct item in stream)
            {
                elements.Add(item);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));
            Assert.That(r2, Is.EqualTo(v2));
        }

        [Test]
        public async Task StreamParam_Send_AnotherStruct()
        {
            AnotherStruct v1 = GetAnotherStruct(1);
            AnotherStruct v2 = GetAnotherStruct(2);

            using var semaphore = new SemaphoreSlim(0);
            await _prx.OpStreamAnotherStructSend0Async(AnotherStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.AnotherStructs.Count, Is.EqualTo(0));
            // Release the semaphore to start streaming elements
            semaphore.Release(1);
            // Wait until the server received all elements
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.AnotherStructs.Count, Is.EqualTo(100));
            Assert.That(_servant.AnotherStructs.All(e => e == v1));

            _servant.AnotherStructs = ImmutableList<AnotherStruct>.Empty;
            await _prx.OpStreamAnotherStructSend1Async(v1, AnotherStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.AnotherStructs.Count, Is.EqualTo(0));
            // Release the semaphore to start streaming elements
            semaphore.Release(1);
            // Wait until the server received all elements
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.AnotherStructs.All(e => e == v1));

            _servant.AnotherStructs = ImmutableList<AnotherStruct>.Empty;
            await _prx.OpStreamAnotherStructSend2Async(v1, v2, AnotherStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.AnotherStructs.Count, Is.EqualTo(0));
            // Release the semaphore to start streaming elements
            semaphore.Release(1);
            // Wait until the server received all elements
            await _servant.EnumerableReceived.WaitAsync();
            Assert.That(_servant.AnotherStructs.Count, Is.EqualTo(100));
            Assert.That(_servant.AnotherStructs.All(e => e == v1));
        }

        [Test]
        public async Task StreamParam_SendAndReceive_AnotherStruct()
        {
            using var semaphore = new SemaphoreSlim(0);

            AnotherStruct v1 = GetAnotherStruct(1);
            AnotherStruct v2 = GetAnotherStruct(2);

            IAsyncEnumerable<AnotherStruct> stream =
                await _prx.OpStreamAnotherStructSendReceive0Async(AnotherStructEnumerable(semaphore, 100, v1));
            semaphore.Release(1);
            var elements = new List<AnotherStruct>();
            await foreach (AnotherStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));

            AnotherStruct r1;
            (r1, stream) = await _prx.OpStreamAnotherStructSendReceive1Async(
                v1,
                AnotherStructEnumerable(semaphore, 100, v1));
            semaphore.Release(1);
            elements = new List<AnotherStruct>();
            await foreach (AnotherStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));

            AnotherStruct r2;
            (r1, r2, stream) = await _prx.OpStreamAnotherStructSendReceive2Async(
                v1,
                v2,
                AnotherStructEnumerable(semaphore, 100, v1));
            semaphore.Release(1);
            elements = new List<AnotherStruct>();
            await foreach (AnotherStruct e in stream)
            {
                elements.Add(e);
            }
            Assert.That(elements.Count, Is.EqualTo(100));
            Assert.That(elements.All(e => e == v1));
            Assert.That(r1, Is.EqualTo(v1));
            Assert.That(r2, Is.EqualTo(v2));
        }

        public class StreamParamOperations : Service, IStreamParamOperations
        {
            public ImmutableList<MyStruct> MyStructs { get; set; } = ImmutableList<MyStruct>.Empty;
            public ImmutableList<AnotherStruct> AnotherStructs { get; set; } = ImmutableList<AnotherStruct>.Empty;

            public SemaphoreSlim EnumerableReceived { get; } = new SemaphoreSlim(0);
            private readonly ReadOnlySequence<byte> _sendBuffer;

            public ValueTask<PipeReader> OpStreamByteReceive0Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(PipeReader.Create(_sendBuffer));

            public ValueTask<(byte, PipeReader)> OpStreamByteReceive1Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, PipeReader.Create(_sendBuffer)));

            public ValueTask<(byte, int, PipeReader)> OpStreamByteReceive2Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, 6, PipeReader.Create(_sendBuffer)));

            public async ValueTask OpStreamByteSend0Async(
                PipeReader p1,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                ReadResult readResult = await p1.ReadAtLeastAsync(256, cancel);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
                Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
                await p1.CompleteAsync();
            }

            public async ValueTask OpStreamByteSend1Async(
                byte p1,
                PipeReader p2,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                ReadResult readResult = await p2.ReadAtLeastAsync(256, cancel);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
                Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
                await p2.CompleteAsync();
            }

            public async ValueTask OpStreamByteSend2Async(
                byte p1,
                int p2,
                PipeReader p3,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                ReadResult readResult = await p3.ReadAtLeastAsync(256, cancel);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(256));
                Assert.That(readResult.Buffer.FirstSpan.SequenceEqual(_sendBuffer.FirstSpan));
                await p3.CompleteAsync();
            }

            public ValueTask<PipeReader> OpStreamByteSendReceive0Async(
                PipeReader p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(p1);

            public ValueTask<(byte, PipeReader)> OpStreamByteSendReceive1Async(
                byte p1,
                PipeReader p2,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((p1, p2));

            public ValueTask<(byte, int, PipeReader)> OpStreamByteSendReceive2Async(
                byte p1,
                int p2,
                PipeReader p3,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((p1, p2, p3));

            public ValueTask OpStreamMyStructSend0Async(
                IAsyncEnumerable<MyStruct> p1,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                Task.Run(async () =>
                {
                    await foreach (MyStruct item in p1)
                    {
                        MyStructs = MyStructs.Add(item);
                    }
                    EnumerableReceived.Release(1);
                },
                cancellationToken: default);
                return default;
            }

            public ValueTask OpStreamMyStructSend1Async(
                MyStruct p1,
                IAsyncEnumerable<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                Task.Run(async () =>
                {
                    await foreach (MyStruct item in p2)
                    {
                        MyStructs = MyStructs.Add(item);
                    }
                    EnumerableReceived.Release(1);
                },
                cancellationToken: default);
                return default;
            }

            public ValueTask OpStreamMyStructSend2Async(
                MyStruct p1,
                MyStruct p2,
                IAsyncEnumerable<MyStruct> p3,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                Task.Run(async () =>
                {
                    await foreach (MyStruct item in p3)
                    {
                        MyStructs = MyStructs.Add(item);
                    }
                    EnumerableReceived.Release(1);
                },
                cancellationToken: default);
                return default;
            }

            public ValueTask OpStreamAnotherStructSend0Async(
                IAsyncEnumerable<AnotherStruct> p1,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                Task.Run(async () =>
                {
                    await foreach (AnotherStruct item in p1)
                    {
                        AnotherStructs = AnotherStructs.Add(item);
                    }
                    EnumerableReceived.Release(1);
                },
                cancellationToken: default);
                return default;
            }

            public ValueTask OpStreamAnotherStructSend1Async(
                AnotherStruct p1,
                IAsyncEnumerable<AnotherStruct> p2,
                Dispatch dispatch, CancellationToken cancel)
            {
                Task.Run(async () =>
                {
                    await foreach (AnotherStruct item in p2)
                    {
                        AnotherStructs = AnotherStructs.Add(item);
                    }
                    EnumerableReceived.Release(1);
                },
                cancellationToken: default);
                return default;
            }

            public ValueTask OpStreamAnotherStructSend2Async(
                AnotherStruct p1,
                AnotherStruct p2,
                IAsyncEnumerable<AnotherStruct> p3,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                Task.Run(async () =>
                {
                    await foreach (AnotherStruct item in p3)
                    {
                        AnotherStructs = AnotherStructs.Add(item);
                    }
                    EnumerableReceived.Release(1);
                },
                cancellationToken: default);
                return default;
            }

            public ValueTask<IAsyncEnumerable<MyStruct>> OpStreamMyStructReceive0Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(MyStructEnumerable(EnumerableReceived, 100, new MyStruct(1, 1)));

            public ValueTask<(MyStruct R1, IAsyncEnumerable<MyStruct> R2)> OpStreamMyStructReceive1Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((
                    new MyStruct(1, 1),
                    MyStructEnumerable(EnumerableReceived, 100, new MyStruct(1, 1))));

            public ValueTask<(MyStruct R1, MyStruct R2, IAsyncEnumerable<MyStruct> R3)> OpStreamMyStructReceive2Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((
                    new MyStruct(1, 1),
                    new MyStruct(2, 2),
                    MyStructEnumerable(EnumerableReceived, 100, new MyStruct(1, 1))));

            public ValueTask<IAsyncEnumerable<MyStruct>> OpStreamMyStructSendReceive0Async(
                IAsyncEnumerable<MyStruct> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<(MyStruct R1, IAsyncEnumerable<MyStruct> R2)> OpStreamMyStructSendReceive1Async(
                MyStruct p1,
                IAsyncEnumerable<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(MyStruct R1, MyStruct R2, IAsyncEnumerable<MyStruct> R3)> OpStreamMyStructSendReceive2Async(
                MyStruct p1,
                MyStruct p2,
                IAsyncEnumerable<MyStruct> p3,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2, p3));

            public ValueTask<IAsyncEnumerable<AnotherStruct>> OpStreamAnotherStructReceive0Async(
                Dispatch dispatch,
                CancellationToken cancel)
            {
                AnotherStruct v1 = GetAnotherStruct(1);
                return new(AnotherStructEnumerable(EnumerableReceived, 100, v1));
            }

            public ValueTask<(AnotherStruct R1, IAsyncEnumerable<AnotherStruct> R2)> OpStreamAnotherStructReceive1Async(
                Dispatch dispatch,
                CancellationToken cancel)
            {
                AnotherStruct v1 = GetAnotherStruct(1);
                return new((v1, AnotherStructEnumerable(EnumerableReceived, 100, v1)));
            }

            public ValueTask<(AnotherStruct R1, AnotherStruct R2, IAsyncEnumerable<AnotherStruct> R3)> OpStreamAnotherStructReceive2Async(
                Dispatch dispatch,
                CancellationToken cancel)
            {
                AnotherStruct v1 = GetAnotherStruct(1);
                AnotherStruct v2 = GetAnotherStruct(2);
                return new((v1, v2, AnotherStructEnumerable(EnumerableReceived, 100, v1)));
            }

            public ValueTask<IAsyncEnumerable<AnotherStruct>> OpStreamAnotherStructSendReceive0Async(
                IAsyncEnumerable<AnotherStruct> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<(AnotherStruct R1, IAsyncEnumerable<AnotherStruct> R2)> OpStreamAnotherStructSendReceive1Async(
                AnotherStruct p1,
                IAsyncEnumerable<AnotherStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(AnotherStruct R1, AnotherStruct R2, IAsyncEnumerable<AnotherStruct> R3)> OpStreamAnotherStructSendReceive2Async(
                AnotherStruct p1,
                AnotherStruct p2,
                IAsyncEnumerable<AnotherStruct> p3,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2, p3));

            public ValueTask OpStreamMyStructSendAndCancel0Async(
                IAsyncEnumerable<MyStruct> p1,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                _ = Task.Run(async () =>
                {
                    var cancellationSource = new CancellationTokenSource();
                    int i = 0;
                    await foreach (MyStruct element in p1.WithCancellation(cancellationSource.Token))
                    {
                        MyStructs = MyStructs.Add(element);
                        if (++i == 20)
                        {
                            break;
                        }
                    }
                    cancellationSource.Cancel();
                    EnumerableReceived.Release();
                },
                CancellationToken.None);
                return default;
            }

            public StreamParamOperations(ReadOnlySequence<byte> buffer) => _sendBuffer = buffer;
        }

        private static async IAsyncEnumerable<MyStruct> MyStructEnumerable(
            SemaphoreSlim semaphore,
            int length,
            MyStruct value)
        {
            await semaphore.WaitAsync();
            for (int i = 0; i < length; ++i)
            {
                yield return value;
            }
        }

        private static async IAsyncEnumerable<AnotherStruct> AnotherStructEnumerable(
            SemaphoreSlim semaphore,
            int length,
            AnotherStruct value)
        {
            await semaphore.WaitAsync();
            for (int i = 0; i < length; ++i)
            {
                yield return value;
            }
        }

        private static List<MyEnum> MyEnumValues
        {
            get
            {
                if (_myEnumValues == null)
                {
                    var myEnumValues = new List<MyEnum>();
                    Array values = Enum.GetValues(typeof(MyEnum));
                    foreach (object? v in values)
                    {
                        myEnumValues.Add((MyEnum)v);
                    }
                    _myEnumValues = myEnumValues;
                }
                return _myEnumValues;
            }
        }
        private static List<MyEnum>? _myEnumValues;

        private static AnotherStruct GetAnotherStruct(int i) =>
            new($"hello-{i}",
                 OperationsPrx.Parse("icerpc://localhost:10000/Operations"),
                 MyEnumValues[i % MyEnumValues.Count],
                 new MyStruct(i, i + 1));
    }
}
