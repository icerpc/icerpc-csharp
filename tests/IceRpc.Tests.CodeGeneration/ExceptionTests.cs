// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public class Exception
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IExceptionOperationsPrx _prx;

        public Exception(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                new ServerOptions()
                {
                    Protocol = protocol,
                    ColocationScope = ColocationScope.Communicator
                });
            _prx = _server.Add("/test", new ExceptionOperations(), IExceptionOperationsPrx.Factory);
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
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
            Assert.IsNotNull(a.InnerException);
            Assert.AreEqual(b, a.InnerException);
            Assert.AreEqual(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)), a.RetryPolicy);
        }

        [Test]
        public void Exception_Operations()
        {
            MyExceptionA? a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAAsync(10));
            Assert.IsNotNull(a);
            Assert.AreEqual(10, a.M1);

            a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAorBAsync(10));
            Assert.IsNotNull(a);
            Assert.AreEqual(10, a.M1);

            MyExceptionB? b = Assert.ThrowsAsync<MyExceptionB>(async () => await _prx.ThrowAorBAsync(0));
            Assert.IsNotNull(b);
            Assert.AreEqual(0, b.M1);
        }

        public class ExceptionOperations : IAsyncExceptionOperations
        {
            public ValueTask ThrowAAsync(int a, Current current, CancellationToken cancel) => throw new MyExceptionA(a);
            public ValueTask ThrowAorBAsync(int a, Current current, CancellationToken cancel)
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
