// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA2000 // TODO Dispose MemoryStream used for Stream params

namespace IceRpc.Tests.CodeGeneration.Stream
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("slic")]
    [TestFixture("coloc")]
    public sealed class StreamParamTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly IStreamsPrx _prx;
        private readonly byte[] _sendBuffer;
        private readonly StreamParamOperations _servant;

        public StreamParamTests(string transport)
        {
            _sendBuffer = new byte[256];
            new Random().NextBytes(_sendBuffer);
            _servant = new StreamParamOperations(_sendBuffer);
            if (transport == "coloc")
            {
                _server = new Server
                {
                    Dispatcher = _servant,
                    Endpoint = TestHelper.GetUniqueColocEndpoint(Protocol.Ice2),
                };
            }
            else
            {
                _server = new Server
                {
                    Dispatcher = _servant,
                    Endpoint = TestHelper.GetTestEndpoint(protocol: Protocol.Ice2),
                };
            }

            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.Endpoint };
            _prx = StreamsPrx.FromConnection(_connection);
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task StreamParam_Byte()
        {
            System.IO.Stream stream;
            byte r1;
            int r2;
            byte[] buffer = new byte[512];

            stream = await _prx.OpStreamByteReceive0Async();
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(0));
            stream.Dispose();

            (r1, stream) = await _prx.OpStreamByteReceive1Async();
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x05));
            stream.Dispose();

            (r1, r2, stream) = await _prx.OpStreamByteReceive2Async();
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x05));
            Assert.That(r2, Is.EqualTo(6));
            stream.Dispose();

            await _prx.OpStreamByteSend0Async(new MemoryStream(_sendBuffer));
            await _prx.OpStreamByteSend1Async(0x08, new MemoryStream(_sendBuffer));
            await _prx.OpStreamByteSend2Async(0x08, 10, new MemoryStream(_sendBuffer));

            stream = await _prx.OpStreamByteSendReceive0Async(new MemoryStream(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(0));
            stream.Dispose();

            (r1, stream) = await _prx.OpStreamByteSendReceive1Async(0x08, new MemoryStream(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x08));
            stream.Dispose();

            (r1, r2, stream) = await _prx.OpStreamByteSendReceive2Async(
                0x08,
                10,
                new MemoryStream(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x08));
            Assert.That(r2, Is.EqualTo(10));
            stream.Dispose();
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

            var semaphore = new SemaphoreSlim(0);
            await _prx.OpStreamMyStructSend0Async(MyStructEnumerable(semaphore, 100, v1));
            Assert.That(_servant.AnotherStructs.Count, Is.EqualTo(0));
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
        public async Task StreamParam_SendAndReceive_MyStruct()
        {
            var semaphore = new SemaphoreSlim(0);

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

            var semaphore = new SemaphoreSlim(0);
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
            var semaphore = new SemaphoreSlim(0);

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
            private readonly byte[] _sendBuffer;

            public ValueTask<System.IO.Stream> OpStreamByteReceive0Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new MemoryStream(_sendBuffer));

            public ValueTask<(byte, System.IO.Stream)> OpStreamByteReceive1Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, new MemoryStream(_sendBuffer)));

            public ValueTask<(byte, int, System.IO.Stream)> OpStreamByteReceive2Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, 6, new MemoryStream(_sendBuffer)));

            public ValueTask OpStreamByteSend0Async(
                System.IO.Stream p1,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                byte[] buffer = new byte[512];
                Assert.That(p1.Read(buffer, 0, 512), Is.EqualTo(256));
                Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
                return default;
            }

            public ValueTask OpStreamByteSend1Async(
                byte p1,
                System.IO.Stream p2,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                byte[] buffer = new byte[512];
                Assert.That(p2.Read(buffer, 0, 512), Is.EqualTo(256));
                Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
                return default;
            }

            public ValueTask OpStreamByteSend2Async(
                byte p1,
                int p2,
                System.IO.Stream p3,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                byte[] buffer = new byte[512];
                Assert.That(p3.Read(buffer, 0, 512), Is.EqualTo(256));
                Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
                return default;
            }

            public ValueTask<System.IO.Stream> OpStreamByteSendReceive0Async(
                System.IO.Stream p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(p1);

            public ValueTask<(byte, System.IO.Stream)> OpStreamByteSendReceive1Async(
                byte p1,
                System.IO.Stream p2,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((p1, p2));

            public ValueTask<(byte, int, System.IO.Stream)> OpStreamByteSendReceive2Async(
                byte p1,
                int p2,
                System.IO.Stream p3,
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

            public StreamParamOperations(byte[] buffer) => _sendBuffer = buffer;
        }

        private static async IAsyncEnumerable<MyStruct> MyStructEnumerable(
            SemaphoreSlim semaphore,
            int lenght,
            MyStruct value)
        {
            await semaphore.WaitAsync();
            for (int i = 0; i < lenght; ++i)
            {
                yield return value;
            }
        }

        private static async IAsyncEnumerable<AnotherStruct> AnotherStructEnumerable(
            SemaphoreSlim semaphore,
            int lenght,
            AnotherStruct value)
        {
            await semaphore.WaitAsync();
            for (int i = 0; i < lenght; ++i)
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
                 OperationsPrx.Parse("ice+tcp://localhost:10000/Operations"),
                 MyEnumValues[i % MyEnumValues.Count],
                 new MyStruct(i, i + 1));
    }
}
