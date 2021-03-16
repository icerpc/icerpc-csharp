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
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public class RetryTests : ClientServerBaseTest
    {
        [Test]
        public async Task Retry_AfterDelay()
        {
            await WithRetryServiceAsync(
                async (service, retry) =>
                {
                    // No retries before timeout kicks-in because the delay specified in the AfterDelay retry policy
                    // is greater than the invocation timeout.
                    retry = retry.Clone(invocationTimeout: TimeSpan.FromMilliseconds(100));
                    Assert.CatchAsync<OperationCanceledException>(
                        async () => await retry.OpRetryAfterDelayAsync(1, 10000));

                    // The second attempt succeed, the elapsed time between attempts must be greater than the delay
                    // specify in the AfterDelay retry policy.
                    service.Attempts = 0;
                    retry = retry.Clone(invocationTimeout: Timeout.InfiniteTimeSpan);
                    long elapsedMilliseconds = await retry.OpRetryAfterDelayAsync(1, 100);
                    Assert.AreEqual(2, service.Attempts);
                    Assert.IsTrue(elapsedMilliseconds >= 100);
                });
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Retry_ConnectionEstablishment(Protocol protocol)
        {
            await using var communicator = new Communicator(
                new Dictionary<string, string>
                {
                    // Speed up windows testing by speeding up the connection failure
                    {"Ice.ConnectTimeout", "1000ms" }
                });

            var prx1 = IRetryReplicatedServicePrx.Parse(GetTestProxy("retry", port: 0, protocol: protocol),
                                                        communicator);
            var prx2 = IRetryReplicatedServicePrx.Parse(GetTestProxy("retry", port: 1, protocol: protocol),
                                                        communicator);
            var prx3 = IRetryReplicatedServicePrx.Parse(GetTestProxy("retry", port: 2, protocol: protocol),
                                                        communicator);

            // Check that we can still connect using a service proxy with 3 endpoints when only one
            // of the target servers is active.
            prx1 = prx1.Clone(endpoints: prx1.Endpoints.Concat(prx2.Endpoints).Concat(prx3.Endpoints));
            Assert.AreEqual(3, prx1.Endpoints.Count);
            foreach (int port in new int[] { 0, 1, 2 })
            {
                await using var server = new Server(
                    communicator,
                    new ServerOptions()
                    {
                        ColocationScope = ColocationScope.None,
                        Endpoints = GetTestEndpoint(port: port, protocol: protocol),
                        Protocol = protocol
                    });

                server.Add("retry", new RetryService());
                server.Activate();
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
            server.Activate();
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

        [TestCase(Protocol.Ice2, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase(Protocol.Ice2, 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase(Protocol.Ice2, 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase(Protocol.Ice2, 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection

        [TestCase(Protocol.Ice1, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase(Protocol.Ice1, 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase(Protocol.Ice1, 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase(Protocol.Ice1, 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        public async Task Retry_Idempotent(Protocol protocol, int failedAttempts, int maxAttempts, bool killConnection)
        {
            Assert.IsTrue(failedAttempts > 0);
            await WithRetryServiceAsync(
                protocol,
                new Dictionary<string, string>
                {
                    {"Ice.InvocationMaxAttempts", $"{maxAttempts}"}
                },
                async (service, retry) =>
                {
                    // Idempotent operations can always be retried, the operation must succeed if the failed attempts are
                    // less than the invocation max attempts configured above.
                    // With Ice1 user exceptions don't carry a retry policy and are not retryable
                    if (failedAttempts < maxAttempts && (protocol == Protocol.Ice2 || killConnection))
                    {
                        await retry.OpIdempotentAsync(failedAttempts, killConnection);
                        Assert.AreEqual(failedAttempts + 1, service.Attempts);
                    }
                    else
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

                        if (protocol == Protocol.Ice2 || killConnection)
                        {
                            Assert.AreEqual(maxAttempts, service.Attempts);
                        }
                        else
                        {
                            Assert.AreEqual(1, service.Attempts);
                        }
                    }
                });
        }

        [Test]
        public async Task Retry_No()
        {
            await WithRetryServiceAsync(
                (service, retry) =>
                {
                    // There is a single attempt because the retry policy doesn't allow to retry
                    Assert.CatchAsync<RetrySystemFailure>(async () => await retry.OpRetryNoAsync());
                    Assert.AreEqual(1, service.Attempts);
                    return Task.CompletedTask;
                });
        }

        [TestCase(Protocol.Ice2, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase(Protocol.Ice2, 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase(Protocol.Ice2, 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase(Protocol.Ice2, 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice2, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection

        [TestCase(Protocol.Ice1, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase(Protocol.Ice1, 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase(Protocol.Ice1, 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase(Protocol.Ice1, 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        [TestCase(Protocol.Ice1, 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        public async Task Retry_NoIdempotent(Protocol protocol, int failedAttempts, int maxAttempts, bool killConnection)
        {
            Assert.IsTrue(failedAttempts > 0);
            await WithRetryServiceAsync(
                protocol,
                new Dictionary<string, string>
                {
                    {"Ice.InvocationMaxAttempts", $"{maxAttempts}"}
                },
                async (service, retry) =>
                {
                    if (failedAttempts > 0 && killConnection)
                    {
                        // Connection failures after a request was sent are not retryable for non idempotent operations
                        Assert.CatchAsync<ConnectionLostException>(
                            async () => await retry.OpNotIdempotentAsync(failedAttempts, killConnection));
                        Assert.AreEqual(1, service.Attempts);
                    }
                    else if (failedAttempts < maxAttempts && (protocol == Protocol.Ice2 || killConnection))
                    {
                        await retry.OpNotIdempotentAsync(failedAttempts, killConnection);
                        Assert.AreEqual(failedAttempts + 1, service.Attempts);
                    }
                    else
                    {
                        Assert.CatchAsync<RetrySystemFailure>(
                            async () => await retry.OpNotIdempotentAsync(failedAttempts, killConnection));
                        if (protocol == Protocol.Ice2 || killConnection)
                        {
                            Assert.AreEqual(maxAttempts, service.Attempts);
                        }
                        else
                        {
                            Assert.AreEqual(1, service.Attempts);
                        }
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

            server1.Activate();
            server2.Activate();

            var prx1 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 0), communicator);
            var prx2 = IRetryReplicatedServicePrx.Parse(GetTestProxy("replicated", port: 1), communicator);

            // The service proxy has 2 endpoints, the request fails using the first endpoint with a exception that
            // has OtherReplica retry policy, it then retries the second endpoint and succeed.
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

                    Task t1 = retry.Clone(label: "conn-1").OpWithDataAsync(2, 5000, data);
                    await Task.Delay(1000); // Ensure the first request it is send before the second request
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
                async (service, retry) =>
                {
                    // Check that only requests with size smaller than RetryRequestMaxSize are retried.
                    byte[] data = Enumerable.Range(0, requestSize).Select(i => (byte)i).ToArray();
                    if (requestSize < maxSize)
                    {
                        await retry.OpWithDataAsync(1, 0, data);
                    }
                    else
                    {
                        // Fails because retry request size limit
                        Assert.ThrowsAsync<RetrySystemFailure>(async () => await retry.OpWithDataAsync(1, 0, data));
                    }
                });
        }

        private async Task WithRetryServiceAsync(
            Protocol protocol,
            Dictionary<string, string> properties,
            Func<RetryService, IRetryServicePrx, Task> closure)
        {
            await using var communicator = new Communicator(properties);
            var service = new RetryService();
            var server = new Server(communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(protocol: protocol),
                    Protocol = protocol
                });
            server.Use(async (current, next, cancel) =>
            {
                service.Attempts++;
                return await next();
            });
            server.Add("retry", service);
            server.Activate();
            var retry = IRetryServicePrx.Parse(GetTestProxy("retry", protocol: protocol), communicator);
            await closure(service, retry);
        }

        private Task WithRetryServiceAsync(Func<RetryService, IRetryServicePrx, Task> closure) =>
            WithRetryServiceAsync(Protocol.Ice2, new Dictionary<string, string>(), closure);

        private Task WithRetryServiceAsync(
            Dictionary<string, string> properties,
            Func<RetryService, IRetryServicePrx, Task> closure) =>
            WithRetryServiceAsync(Protocol.Ice2, properties, closure);

        internal class RetryService : IAsyncRetryService
        {
            internal int Attempts;
            private readonly Stopwatch _stopwatch = new Stopwatch();

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
                    await Task.Delay(delay, cancel);
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
