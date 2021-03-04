// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(scope: ParallelScope.All)]
    public class RetryTests : ClientServerBaseTest
    {
        RetryService Service;
        IRetryServicePrx Retry;

        public RetryTests()
        {
            Service = new RetryService();
            Server.Use(async (current, next, cancel) =>
                {
                    Service.Attempts++;
                    return await next();
                });
            Retry = Server.Add("retry", Service, IRetryServicePrx.Factory);
        }

        [Test]
        public void Retry_Cancelation()
        {
            // No more than 2 retries before timeout kicks-in
            Retry = Retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(500));
            Assert.CatchAsync<OperationCanceledException>(async () => await Retry.OpIdempotentAsync(4));
            Assert.AreEqual(3, Service.Attempts);
        }

        [Test]
        public void Retry_KillConnection()
        {
            Assert.ThrowsAsync<ConnectionLostException>(async () => await Retry.OpAsync(true));
            Assert.AreEqual(1, Service.Attempts);
        }

        [Test]
        public void Retry_OpNotIdempotent()
        {
            Assert.ThrowsAsync<UnhandledException>(async () => await Retry.OpNotIdempotentAsync());
            Assert.AreEqual(1, Service.Attempts);
        }

        [Test]
        public void Retry_SystemException()
        {
            Assert.ThrowsAsync<RetrySystemFailure>(async () => await Retry.OpSystemExceptionAsync());
            Assert.AreEqual(1, Service.Attempts);
        }

        [Test]
        public async Task Retry_FixedReference()
        {
            var server = new Server(Communicator, new ServerOptions() { Protocol = Protocol.Ice2 });
            var bidir = server.AddWithUUID(new Bidir(), IRetryBidirServicePrx.Factory);
            (await Retry.GetConnectionAsync()).Server = server;
            await Retry.OpBidirRetryAsync(bidir);
        }

        [Test]
        public async Task Retry_AfterDelay()
        {
            // No retries before timeout kicks-in
            Retry = Retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(400));
            Assert.CatchAsync<OperationCanceledException>(async () => await Retry.OpAfterDelayAsync(2, 600));

            // 5 attempts before timeout kicks-in
            Service.Attempts = 0;
            Retry = Retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(1000));
            await Retry.OpAfterDelayAsync(4, 50);
            Assert.AreEqual(5, Service.Attempts);

            // No more than 5 invocation attempts with the default settings
            Service.Attempts = 0;
            Retry = Retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(500));
            Assert.ThrowsAsync<RetrySystemFailure>(async () => await Retry.OpAfterDelayAsync(5, 50));
            Assert.AreEqual(5, Service.Attempts);
        }

        [Test]
        public async Task Retry_OtherReplica()
        {
            await using var server1 = new Server(
                Communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 1)
                });

            await using var server2 = new Server(
                Communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 2)
                });

            server1.Add("replicated", new Replicated(fail: true));
            server2.Add("replicated", new Replicated(fail: false));

            await server1.ActivateAsync();
            await server2.ActivateAsync();

            var prx1 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 1), Communicator);
            var prx2 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 2), Communicator);

            Assert.ThrowsAsync<RetrySystemFailure>(async () => await prx1.OtherReplicaAsync());
            Assert.DoesNotThrowAsync(async () => await prx2.OtherReplicaAsync());

            prx1 = prx1.Clone(endpoints: prx1.Endpoints.Concat(prx2.Endpoints));
            Assert.DoesNotThrowAsync(async () => await prx1.OtherReplicaAsync());
        }

        [TestCase(1024, 1024)]
        [TestCase(1024, 2048)]
        public async Task Retry_RetryRequestSizeMax(int maxSize, int requestSize)
        {
            await using var communicator = new Communicator(
                new Dictionary<string, string>
                {
                    { "Ice.RetryRequestMaxSize", $"{maxSize}" }
                });

            var retry = IRetryServicePrx.Parse(GetTestProxy("retry"), communicator);
            byte[] data = Enumerable.Range(0, requestSize).Select(i => (byte)i).ToArray();
            if (maxSize <= requestSize)
            {
                // Fails because retry request size limit
                Assert.ThrowsAsync<RetrySystemFailure>(async () => await retry.OpWithDataAsync(1, 0, data));
            }
            else
            {
                Assert.DoesNotThrowAsync(async () => await retry.OpWithDataAsync(1, 0, data));
            }
        }

        [Test]
        public async Task Retry_RetryBufferMaxSize()
        {
            await using var communicator = new Communicator(
                new Dictionary<string, string>
                {
                    { "Ice.RetryBufferMaxSize", "2048" }
                });

            byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
            var retry = IRetryServicePrx.Parse(GetTestProxy("retry"), communicator);

            // Use two connections to simulate two concurrent retries, the first should succeed
            // and the second should fail because the buffer size max.

            Task t1 = retry.Clone(label: "conn-1").OpWithDataAsync(2, 1000, data);
            await Task.Delay(100); // Ensure the first request it is send before the second request
            Task t2 = retry.Clone(label: "conn-2").OpWithDataAsync(2, 0, data);

            Assert.DoesNotThrowAsync(async () => await t1);
            Assert.ThrowsAsync<RetrySystemFailure>(async () => await t2);

            await retry.Clone(label: "conn-1").OpWithDataAsync(2, 100, data);
        }

        internal class RetryService : IAsyncRetryService
        {
            internal int Attempts;
            private int _counter;

            public ValueTask OpAfterDelayAsync(int retries, int delay, Current current, CancellationToken cancel)
            {
                if (retries >= Attempts)
                {
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                }
                return default;
            }

            public ValueTask OpAsync(bool kill, Current current, CancellationToken cancel)
            {
                if (kill)
                {
                    current.Connection.AbortAsync();
                }
                return default;
            }

            public async ValueTask OpBidirRetryAsync(
                IRetryBidirServicePrx bidir,
                Current current,
                CancellationToken cancel)
            {
                bidir = bidir.Clone(fixedConnection: current.Connection);
                Assert.ThrowsAsync< ServiceNotFoundException>(
                    async () => await bidir.OtherReplicaAsync(cancel: CancellationToken.None));

                // With Ice1 the exception is not retryable, with Ice2 we can retry using the existing connection
                // because the exception uses the AfterDelay retry policy.
                Assert.IsTrue(current.Protocol == Protocol.Ice2);
                await bidir.AfterDelayAsync(2, cancel: CancellationToken.None);
            }

            public ValueTask OpIdempotentAsync(int nRetry, Current current, CancellationToken cancel)
            {
                int[] delays = new int[] { 0, 1, 10000 };
                if (nRetry >= Attempts)
                {
                    throw new RetrySystemFailure(
                        RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delays[(Attempts - 1) % 3])));
                }
                return default;
            }

            public ValueTask OpNotIdempotentAsync(Current current, CancellationToken cancel) =>
                throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));

            public ValueTask OpSystemExceptionAsync(Current current, CancellationToken cancel) =>
                throw new RetrySystemFailure();

            public ValueTask OpWithDataAsync(
                int retries,
                int delay,
                byte[] data,
                Current current,
                CancellationToken cancel)
            {
                if (retries >= Attempts)
                {
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                }
                return default;
            }
        }
    }

    public class Bidir : IAsyncRetryBidirService
    {
        private int _n;

        public ValueTask AfterDelayAsync(int n, Current current, CancellationToken cancel)
        {
            if (++_n < n)
            {
                throw new ServiceNotFoundException(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(10)));
            }
            _n = 0;
            return default;
        }

        public ValueTask OtherReplicaAsync(Current current, CancellationToken cancel) =>
            throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
    }

    public class Replicated : IAsyncRetryReplicatedService
    {
        private bool _fail;
        public Replicated(bool fail) => _fail = fail;

        public ValueTask OtherReplicaAsync(Current current, CancellationToken cancel)
        {
            if (_fail)
            {
                throw new RetrySystemFailure(RetryPolicy.OtherReplica);
            }
            return default;
        }
    }
}
