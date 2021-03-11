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
    [Timeout(10000)]
    [Parallelizable(ParallelScope.All)]
    public class RetryTests : ClientServerBaseTest
    {
        [Test]
        public async Task Retry_Idempotent()
        {
            await WithRetryServiceAsync(async (service, retry) =>
            {
                // No more than 5 attempts with default configuration
                Connection connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                Assert.CatchAsync<RetrySystemFailure>(async () => await retry.OpIdempotentAsync(5, 0, false));
                Assert.AreEqual(5, service.Attempts);

                // No more than 5 attempts with default configuration
                service.Attempts = 0;
                connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                Assert.CatchAsync<ConnectionLostException>(async () => await retry.OpIdempotentAsync(5, 0, true));
                Assert.AreEqual(5, service.Attempts);

                // 4 failures the 5th attempt should succeed
                service.Attempts = 0;
                connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                await retry.OpIdempotentAsync(4, 0, false);
                Assert.AreEqual(5, service.Attempts);

                // 4 failures the 5th attempt should succeed
                service.Attempts = 0;
                connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                await retry.OpIdempotentAsync(4, 0, true);
                Assert.AreEqual(5, service.Attempts);
            });
        }

        [Test]
        public async Task Retry_NotIdempotent()
        {
            await WithRetryServiceAsync(async (service, retry) =>
            {
                // No more than 5 attempts with default configuration
                Connection connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                Assert.CatchAsync<RetrySystemFailure>(async () => await retry.OpNotIdempotentAsync(5, 0, false));
                Assert.AreEqual(5, service.Attempts);

                // Connection failure is not retryable for non idempotent operation
                service.Attempts = 0;
                connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                Assert.CatchAsync<ConnectionLostException>(async () => await retry.OpNotIdempotentAsync(5, 0, true));
                Assert.AreEqual(1, service.Attempts);

                // 4 failures the 5th attempt should succeed
                service.Attempts = 0;
                connection = await retry.GetConnectionAsync();
                Assert.IsNotNull(connection);
                await retry.OpNotIdempotentAsync(4, 0, false);
                Assert.AreEqual(5, service.Attempts);
            });
        }

        [Test]
        public async Task Retry_FixedReference()
        {
            await WithRetryServiceAsync(async (service, retry) =>
            {
                await using var communicator = new Communicator();
                await using var server = new Server(communicator,
                    new ServerOptions()
                    {
                        ColocationScope = ColocationScope.None,
                        Protocol = Protocol.Ice2
                    });
                var bidir = server.AddWithUUID(new Bidir(), IRetryBidirServicePrx.Factory);
                (await retry.GetConnectionAsync()).Server = server;
                await retry.OpBidirRetryAsync(bidir);
            });
        }

        [Test]
        public async Task Retry_AfterDelay()
        {
            await WithRetryServiceAsync(async (service, retry) =>
            {
                // No retries before timeout kicks-in
                retry = retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(100));
                Assert.CatchAsync<OperationCanceledException>(
                    async () => await retry.OpNotIdempotentAsync(2, 1000, false));

                // The second attempt succeed
                service.Attempts = 0;
                retry = retry.Clone(invocationTimeout: Timeout.InfiniteTimeSpan);
                await retry.OpNotIdempotentAsync(1, 50, false);
                Assert.AreEqual(2, service.Attempts);
            });
        }

        [Test]
        public async Task Retry_OtherReplica()
        {
            await using var communicator = new Communicator();
            await using var server1 = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 0)
                });

            await using var server2 = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 1)
                });

            server1.Add("replicated", new Replicated(fail: true));
            server2.Add("replicated", new Replicated(fail: false));

            await server1.ActivateAsync();
            await server2.ActivateAsync();

            var prx1 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 0), communicator);
            var prx2 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 1), communicator);

            Assert.ThrowsAsync<RetrySystemFailure>(async () => await prx1.OtherReplicaAsync());
            Assert.DoesNotThrowAsync(async () => await prx2.OtherReplicaAsync());

            prx1 = prx1.Clone(endpoints: prx1.Endpoints.Concat(prx2.Endpoints));
            Assert.DoesNotThrowAsync(async () => await prx1.OtherReplicaAsync());
        }

        [TestCase(1024, 1024)]
        [TestCase(1024, 2048)]
        public async Task Retry_RetryRequestSizeMax(int maxSize, int requestSize)
        {
            await WithRetryServiceAsync(
                new Dictionary<string, string>
                {
                    { "Ice.RetryRequestMaxSize", $"{maxSize}" }
                },
                (service, retry) =>
                {
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
                    return Task.CompletedTask;
                });
        }

        [Test]
        public async Task Retry_RetryBufferMaxSize()
        {
            await WithRetryServiceAsync(
                new Dictionary<string, string>
                {
                    { "Ice.RetryBufferMaxSize", "2048" }
                },
                async (service, retry) =>
                {

                    byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
                    // Use two connections to simulate two concurrent retries, the first should succeed
                    // and the second should fail because the buffer size max.

                    Task t1 = retry.Clone(label: "conn-1").OpWithDataAsync(2, 1000, data);
                    await Task.Delay(100); // Ensure the first request it is send before the second request
                    Task t2 = retry.Clone(label: "conn-2").OpWithDataAsync(2, 0, data);

                    Assert.DoesNotThrowAsync(async () => await t1);
                    Assert.ThrowsAsync<RetrySystemFailure>(async () => await t2);

                    await retry.Clone(label: "conn-1").OpWithDataAsync(2, 100, data);
                });
        }

        [Test]
        public async Task Retry_ConnectionEstablishment()
        {
            await using var communicator = new Communicator(
                new Dictionary<string, string>
                {
                    // Speed up windows testing by speeding up the connection failure
                    {"Ice.ConnectTimeout", "200ms" }
                });

            var prx1 = IRetryReplicatedServicePrx.Parse(GetTestProxy("retry", port: 0), communicator);
            var prx2 = IRetryReplicatedServicePrx.Parse(GetTestProxy("retry", port: 1), communicator);
            var prx3 = IRetryReplicatedServicePrx.Parse(GetTestProxy("retry", port: 2), communicator);

            prx1 = prx1.Clone(endpoints: prx1.Endpoints.Concat(prx2.Endpoints).Concat(prx3.Endpoints));

            foreach (int port in new int[] { 0, 1, 2 })
            {
                await using var server = new Server(
                    communicator,
                    new ServerOptions()
                    {
                        ColocationScope = ColocationScope.None,
                        Endpoints = GetTestEndpoint(port: port)
                    });

                server.Add("retry", new RetryService());
                await server.ActivateAsync();
                Assert.DoesNotThrowAsync(async () => await prx1.IcePingAsync());
            }
        }

        private async Task WithRetryServiceAsync(
            Dictionary<string, string> properties,
            Func<RetryService, IRetryServicePrx, Task> closure)
        {
            await using var communicator = new Communicator(properties);
            var service = new RetryService();
            var server = new Server(communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint()
                });
            server.Use(async (current, next, cancel) =>
            {
                service.Attempts++;
                return await next();
            });
            server.Add("retry", service);
            await server.ActivateAsync();
            var retry = IRetryServicePrx.Parse(GetTestProxy("retry"), communicator);
            await closure(service, retry);
        }

        private Task WithRetryServiceAsync(Func<RetryService, IRetryServicePrx, Task> closure) =>
            WithRetryServiceAsync(new Dictionary<string, string>(), closure);

        internal class RetryService : IAsyncRetryService
        {
            internal int Attempts;

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

            public ValueTask OpIdempotentAsync(int retries, int delay, bool kill, Current current, CancellationToken cancel)
            {
                if (Attempts <= retries)
                {
                    if (kill)
                    {
                        current.Connection.AbortAsync();
                    }
                    else
                    {
                        throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                    }
                }
                return default;
            }

            public ValueTask OpNotIdempotentAsync(int retries, int delay, bool kill, Current current, CancellationToken cancel)
            {
                if (Attempts <= retries)
                {
                    if (kill)
                    {
                        current.Connection.AbortAsync();
                    }
                    else
                    {
                        throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                    }
                }
                return default;
            }

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
        private readonly bool _fail;
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
