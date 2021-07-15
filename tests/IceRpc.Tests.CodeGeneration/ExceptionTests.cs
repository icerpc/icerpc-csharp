// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public sealed class Exception : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly IExceptionOperationsPrx _prx;

        public Exception(Protocol protocol)
        {
            var classFactory = new ClassFactory(new Assembly[] { typeof(Exception).Assembly });

            _server = new Server
            {
                Dispatcher = new ExceptionOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol),
                ConnectionOptions = new ServerConnectionOptions { ClassFactory = classFactory }
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.ProxyEndpoint,
                Options = new ClientConnectionOptions() { ClassFactory = classFactory }
            };
            _prx = IExceptionOperationsPrx.FromConnection(_connection);
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public void Exception_Constructors()
        {
            var a = new MyExceptionA(RetryPolicy.NoRetry);
            Assert.AreEqual(RetryPolicy.NoRetry, a.RetryPolicy);

            a = new MyExceptionA(RetryPolicy.OtherReplica);
            Assert.AreEqual(RetryPolicy.OtherReplica, a.RetryPolicy);

            a = new MyExceptionA(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            Assert.AreEqual(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)), a.RetryPolicy);

            a = new MyExceptionA();
            Assert.AreEqual(RetryPolicy.NoRetry, a.RetryPolicy);

            Assert.AreEqual(10, new MyExceptionA(10).M1);

            var b = new MyExceptionB("my exception B", 20, retryPolicy: RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            a = new MyExceptionA("my exception A", 10, b, RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            Assert.AreEqual("my exception A", a.Message);
            Assert.AreEqual(10, a.M1);
            Assert.That(a, Is.Not.Null);
            Assert.AreEqual(b, a.InnerException);
            Assert.AreEqual(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)), a.RetryPolicy);
        }

        [Test]
        public void Exception_Operations()
        {
            MyExceptionA? a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAAsync(10));
            Assert.That(a, Is.Not.Null);
            Assert.AreEqual(10, a.M1);

            a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAorBAsync(10));
            Assert.That(a, Is.Not.Null);
            Assert.AreEqual(10, a.M1);

            MyExceptionB? b = Assert.ThrowsAsync<MyExceptionB>(async () => await _prx.ThrowAorBAsync(0));
            Assert.That(b, Is.Not.Null);
            Assert.AreEqual(0, b.M1);
        }

        public class ExceptionOperations : Service, IExceptionOperations
        {
            public ValueTask ThrowAAsync(int a, Dispatch dispatch, CancellationToken cancel) => throw new MyExceptionA(a);
            public ValueTask ThrowAorBAsync(int a, Dispatch dispatch, CancellationToken cancel)
            {
                if (a > 0)
                {
                    throw new MyExceptionA(a);
                }
                else
                {
                    throw new MyExceptionB(a);
                }
            }
        }
    }
}
