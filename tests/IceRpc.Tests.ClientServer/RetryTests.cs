// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public async Task Retry_AfterDelay()
        {
            await WithRetryServiceAsync(
                async (service, retry) =>
                {
                    // No retries before timeout kicks-in
                    retry = retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(100));
                    Assert.CatchAsync<OperationCanceledException>(
                        async () => await retry.OpRetryAfterDelayAsync(1, 10000));

                    // The second attempt succeed
                    service.Attempts = 0;
                    retry = retry.Clone(invocationTimeout: Timeout.InfiniteTimeSpan);
                    long elapsedMilliseconds = await retry.OpRetryAfterDelayAsync(1, 100);
                    Assert.AreEqual(2, service.Attempts);
                    Assert.IsTrue(elapsedMilliseconds > 100);
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

        [Test]
        public async Task Retry_FixedReference()
        {
            await using var communicator = new Communicator();
            await using var server = new Server(communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Protocol = Protocol.Ice2,
                    Endpoints = GetTestEndpoint()
                });
            await server.ActivateAsync();
            var proxy = server.Add("bidir", new Bidir(), IRetryBidirServicePrx.Factory);

            Connection connection = await proxy.GetConnectionAsync();
            connection.Server = server;
            var bidir = proxy.Clone(fixedConnection: connection);

            Assert.ThrowsAsync< ServiceNotFoundException>(
                async () => await bidir.OtherReplicaAsync(cancel: CancellationToken.None));

            // With Ice1 the exception is not retryable, with Ice2 we can retry using the existing connection
            // because the exception uses the AfterDelay retry policy.
            Assert.IsTrue(bidir.Protocol == Protocol.Ice2);
            await bidir.AfterDelayAsync(2);
        }

        [TestCase(1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase(1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase(1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase(1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase(5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase(5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase(0, 1, false)] // 0 failures, 1 max attempts, don't kill the connection
        [TestCase(4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        public async Task Retry_Idempotent(int failedAttempts, int maxAttempts, bool killConnection)
        {
            await WithRetryServiceAsync(
                new Dictionary<string, string>
                {
                    {"Ice.InvocationMaxAttempts", $"{maxAttempts}"}
                },
                async (service, retry) =>
                {
                    if (failedAttempts >= maxAttempts)
                    {
                        if (killConnection)
                        {
                            Assert.CatchAsync<ConnectionLostException>(
                                async () => await retry.OpIdempotentAsync(failedAttempts, killConnection));
                        }
                        else
                        {
                            Assert.CatchAsync<RetrySystemFailure>(
                                async () => await retry.OpIdempotentAsync(failedAttempts, killConnection));
                        }
                        Assert.AreEqual(maxAttempts, service.Attempts);
                    }
                    else
                    {
                        await retry.OpIdempotentAsync(failedAttempts, killConnection);
                        Assert.AreEqual(failedAttempts + 1, service.Attempts);
                    }
                });
        }

        [Test]
        public async Task Retry_No()
        {
            await WithRetryServiceAsync(
                (service, retry) =>
                {
                    // There is a single attempt becasuse the retry policy doesn't allow to retry
                    Assert.CatchAsync<RetrySystemFailure>(async () => await retry.OpRetryNoAsync());
                    Assert.AreEqual(1, service.Attempts);
                    return Task.CompletedTask;
                });
        }

        [TestCase(1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase(1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase(1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase(1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase(5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase(5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase(0, 1, false)] // 0 failures, 1 max attempts, don't kill the connection
        [TestCase(4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        public async Task Retry_NoIdempotent(int failedAttempts, int maxAttempts, bool killConnection)
        {
            await WithRetryServiceAsync(
                new Dictionary<string, string>
                {
                    {"Ice.InvocationMaxAttempts", $"{maxAttempts}"}
                },
                async (service, retry) =>
                {
                    if (failedAttempts > 0 && killConnection)
                    {
                        // Connection failure after sent is not retryable for non idempotent operation
                        Assert.CatchAsync<ConnectionLostException>(
                            async () => await retry.OpNotIdempotentAsync(failedAttempts, killConnection));
                        Assert.AreEqual(1, service.Attempts);
                    }
                    else if (failedAttempts >= maxAttempts)
                    {
                        Assert.CatchAsync<RetrySystemFailure>(
                            async () => await retry.OpNotIdempotentAsync(failedAttempts, killConnection));
                        Assert.AreEqual(maxAttempts, service.Attempts);
                    }
                    else
                    {
                        await retry.OpNotIdempotentAsync(failedAttempts, killConnection);
                        Assert.AreEqual(failedAttempts + 1, service.Attempts);
                    }
                });
        }

        [Test]
        public async Task Retry_OtherReplica()
        {
            await using var communicator = new Communicator();
            var calls = new List<string>();
            await using var server1 = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 0)
                });

            server1.Use(async (current, next, cancel) =>
                        {
                            calls.Add("server1");
                            return await next();
                        });

            await using var server2 = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 1)
                });
            server2.Use(async (current, next, cancel) =>
                        {
                            calls.Add("server2");
                            return await next();
                        });

            server1.Add("replicated", new Replicated(fail: true));
            server2.Add("replicated", new Replicated(fail: false));

            await server1.ActivateAsync();
            await server2.ActivateAsync();

            var prx1 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 0), communicator);
            var prx2 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 1), communicator);

            Assert.ThrowsAsync<RetrySystemFailure>(async () => await prx1.OtherReplicaAsync());
            Assert.AreEqual("server1", calls[0]);
            Assert.AreEqual(1, calls.Count);

            calls.Clear();
            Assert.DoesNotThrowAsync(async () => await prx2.OtherReplicaAsync());
            Assert.AreEqual("server2", calls[0]);
            Assert.AreEqual(1, calls.Count);

            calls.Clear();
            prx1 = prx1.Clone(endpoints: prx1.Endpoints.Concat(prx2.Endpoints));
            Assert.DoesNotThrowAsync(async () => await prx1.OtherReplicaAsync());
            Assert.AreEqual("server1", calls[0]);
            Assert.AreEqual("server2", calls[1]);
            Assert.AreEqual(2, calls.Count);
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
                    // Use two connections to simulate two concurrent requests, the first should succeed
                    // and the second should fail because the buffer size max.

                    Task t1 = retry.Clone(label: "conn-1").OpWithDataAsync(2, 1000, data);
                    await Task.Delay(100); // Ensure the first request it is send before the second request
                    Task t2 = retry.Clone(label: "conn-2").OpWithDataAsync(2, 0, data);

                    Assert.DoesNotThrowAsync(async () => await t1);
                    Assert.ThrowsAsync<RetrySystemFailure>(async () => await t2);

                    await retry.Clone(label: "conn-1").OpWithDataAsync(2, 100, data);
                });
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
            private Stopwatch _stopwatch = new Stopwatch();

            public ValueTask OpIdempotentAsync(
                int failedAttempts,
                bool killConnection,
                Current current,
                CancellationToken cancel)
            {
                if (Attempts <= failedAttempts)
                {
                    if (killConnection)
                    {
                        current.Connection.AbortAsync();
                    }
                    else
                    {
                        throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.Zero));
                    }
                }
                return default;
            }

            public ValueTask OpNotIdempotentAsync(
                int failedAttempts,
                bool killConnection,
                Current current,
                CancellationToken cancel)
            {
                if (Attempts <= failedAttempts)
                {
                    if (killConnection)
                    {
                        current.Connection.AbortAsync();
                    }
                    else
                    {
                        throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.Zero));
                    }
                }
                return default;
            }

            public async ValueTask OpWithDataAsync(
                int failedAttempts,
                int delay,
                byte[] data,
                Current current,
                CancellationToken cancel)
            {
                if (failedAttempts >= Attempts)
                {
                    await Task.Delay(delay);
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
            }

            public ValueTask<long> OpRetryAfterDelayAsync(
                int failedAttempts,
                int delay,
                Current current,
                CancellationToken cancel)
            {
                if (failedAttempts >= Attempts)
                {
                    _stopwatch.Restart();
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                }
                return new(_stopwatch.ElapsedMilliseconds);
            }

            public ValueTask OpRetryNoAsync(Current current, CancellationToken cancel) =>
                throw new RetrySystemFailure(RetryPolicy.NoRetry);
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
