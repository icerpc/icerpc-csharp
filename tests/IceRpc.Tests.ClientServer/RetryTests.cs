// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(5000)]
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
                    var invocation = new Invocation { Timeout = TimeSpan.FromMilliseconds(100) };
                    Assert.CatchAsync<OperationCanceledException>(
                        async () => await retry.OpRetryAfterDelayAsync(1, 10000, invocation));

                    // The second attempt succeed, the elapsed time between attempts must be greater than the delay
                    // specify in the AfterDelay retry policy.
                    service.Attempts = 0;
                    invocation.Timeout = Timeout.InfiniteTimeSpan;
                    await retry.OpRetryAfterDelayAsync(1, 100, invocation);
                    Assert.AreEqual(2, service.Attempts);
                });
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Retry_ConnectionEstablishment(Protocol protocol)
        {
            await using ConnectionPool pool = CreateConnectionPool();
            Pipeline pipeline = CreatePipeline(pool);

            var prx1 = RetryReplicatedTestPrx.Parse(GetTestProxy("/retry", port: 0, protocol: protocol), pipeline);
            var prx2 = RetryReplicatedTestPrx.Parse(GetTestProxy("/retry", port: 1, protocol: protocol), pipeline);
            var prx3 = RetryReplicatedTestPrx.Parse(GetTestProxy("/retry", port: 2, protocol: protocol), pipeline);

            // Check that we can still connect using a service proxy with 3 endpoints when only one
            // of the target servers is active.
            prx1.Proxy.AltEndpoints = ImmutableList.Create(prx2.Proxy.Endpoint!, prx3.Proxy.Endpoint!);
            foreach (int port in new int[] { 0, 1, 2 })
            {
                await using var server = new Server
                {
                    Dispatcher = new RetryTest(),
                    Endpoint = GetTestEndpoint(port: port, protocol: protocol),
                    ServerTransport = new ServerTransport().UseTcp(),
                    ConnectionOptions = new() { CloseTimeout = TimeSpan.FromMinutes(5) }
                };
                server.Listen();
                Assert.DoesNotThrowAsync(async () => await prx1.IcePingAsync());
            }
        }

        [Test]
        public async Task Retry_EndpointlessProxy()
        {
            await using ConnectionPool pool = CreateConnectionPool();
            Pipeline pipeline = CreatePipeline(pool);

            await using var server = new Server
            {
                Dispatcher = new Bidir(),
                Endpoint = GetTestEndpoint(),
                ServerTransport = new ServerTransport().UseTcp()
            };
            server.Listen();

            var retryBidir = RetryBidirTestPrx.FromPath("/");
            retryBidir.Proxy.Endpoint = server.Endpoint;
            retryBidir.Proxy.Invoker = pipeline;
            await retryBidir.IcePingAsync();

            var bidir = new RetryBidirTestPrx(retryBidir.Proxy.Clone()); // keeps Connection
            bidir.Proxy.Endpoint = null; // endpointless proxy with a connection

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await bidir.OtherReplicaAsync(cancel: CancellationToken.None));

            // With Ice1 the exception is not retryable, with Ice2 we can retry using the existing connection
            // because the exception uses the AfterDelay retry policy.
            Assert.That(bidir.Proxy.Protocol, Is.EqualTo(Protocol.Ice2));
            await bidir.AfterDelayAsync(2);
        }

        [TestCase(Protocol.Ice1, 2)]
        [TestCase(Protocol.Ice1, 10)]
        [TestCase(Protocol.Ice1, 20)]
        [TestCase(Protocol.Ice2, 2)]
        [TestCase(Protocol.Ice2, 10)]
        [TestCase(Protocol.Ice2, 20)]
        public async Task Retry_GracefulClose(Protocol protocol, int maxQueue)
        {
            await WithRetryServiceAsync(protocol, null, async (service, retry) =>
            {
                // Remote case: send multiple OpWithData, followed by a close and followed by multiple OpWithData.
                // The goal is to make sure that none of the OpWithData fail even if the server closes the
                // connection gracefully in between.
                byte[] seq = new byte[1024 * 10];

                await retry.IcePingAsync();
                var results = new List<Task>();
                for (int i = 0; i < maxQueue; ++i)
                {
                    results.Add(retry.OpWithDataAsync(-1, 0, seq));
                }

                Task shutdownTask = service.Connection!.ShutdownAsync();

                for (int i = 0; i < maxQueue; i++)
                {
                    results.Add(retry.OpWithDataAsync(-1, 0, seq));
                }

                await Task.WhenAll(results);
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
        public async Task Retry_Idempotent(Protocol protocol, int failedAttempts, int maxAttempts, bool killConnection)
        {
            Assert.That(failedAttempts, Is.GreaterThan(0));
            await WithRetryServiceAsync(
                protocol,
                (pipeline, pool) => pipeline.UseRetry(new RetryOptions { MaxAttempts = maxAttempts }).UseBinder(pool),
                async (service, retry) =>
                {
                    // Idempotent operations can always be retried, the operation must succeed if the failed attempts
                    // are less than the invocation max attempts configured above.
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
            Assert.That(failedAttempts, Is.GreaterThan(0));
            await WithRetryServiceAsync(
                protocol,
                (pipeline, pool) => pipeline.UseRetry(new RetryOptions { MaxAttempts = maxAttempts }).UseBinder(pool),
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
            await using ConnectionPool pool = CreateConnectionPool();
            Pipeline pipeline = CreatePipeline(pool);
            var calls = new List<string>();
            await WithReplicatedRetryServiceAsync(
                replicas: 2,
                (servers, routers) =>
                {
                    for (int i = 0; i < routers.Length; ++i)
                    {
                        routers[i].Use(next => new InlineDispatcher(
                            async (request, cancel) =>
                            {
                                calls.Add(request.Connection.LocalEndpoint!.ToString());
                                return await next.DispatchAsync(request, cancel);
                            }));
                        servers[i].Dispatcher = routers[i];
                        servers[i].Listen();
                    }

                    routers[0].Map("/replicated", new Replicated(fail: true));
                    routers[1].Map("/replicated", new Replicated(fail: false));

                    var prx1 = RetryReplicatedTestPrx.Parse(GetTestProxy("/replicated", port: 0), pipeline);
                    var prx2 = RetryReplicatedTestPrx.Parse(GetTestProxy("/replicated", port: 1), pipeline);

                    // The service proxy has 2 endpoints, the request fails using the first endpoint with a retryable
                    //  exception that has OtherReplica retry policy, it then retries the second endpoint and succeed.
                    calls.Clear();
                    prx1.Proxy.AltEndpoints = ImmutableList.Create(prx2.Proxy.Endpoint!);
                    Assert.DoesNotThrowAsync(async () => await prx1.OtherReplicaAsync());
                    Assert.AreEqual(servers[0].ToString(), calls[0]);
                    Assert.AreEqual(servers[1].ToString(), calls[1]);
                    Assert.AreEqual(2, calls.Count);
                });
        }

        [Test]
        public async Task Retry_ReportLastFailure()
        {
            await using ConnectionPool pool = CreateConnectionPool();
            pool.PreferExistingConnection = false;
            Pipeline pipeline = CreatePipeline(pool);

            var calls = new List<string>();
            await WithReplicatedRetryServiceAsync(
                replicas: 3,
                (servers, routers) =>
                {
                    foreach (Router router in routers)
                    {
                        router.Use(next => new InlineDispatcher(
                            async (request, cancel) =>
                            {
                                calls.Add(request.Connection.LocalEndpoint!.ToString());
                                return await next.DispatchAsync(request, cancel);
                            }));
                    }
                    routers[1].Map("/replicated", new Replicated(fail: true));
                    routers[2].Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            await request.Connection.CloseAsync("forcefully close connection!");
                            return await next.DispatchAsync(request, cancel);
                        }));

                    for (int i = 0; i < servers.Length; ++i)
                    {
                        servers[i].Dispatcher = routers[i];
                        servers[i].Listen();
                    }
                    var prx1 = RetryReplicatedTestPrx.Parse(GetTestProxy("/replicated", port: 0), pipeline);
                    var prx2 = RetryReplicatedTestPrx.Parse(GetTestProxy("/replicated", port: 1), pipeline);
                    var prx3 = RetryReplicatedTestPrx.Parse(GetTestProxy("/replicated", port: 2), pipeline);

                    // The first replica fails with ServiceNotFoundException exception the second replica fails with
                    // RetrySystemFailure the last failure should be reported.
                    calls.Clear();
                    var prx = new RetryReplicatedTestPrx(prx1.Proxy.Clone());
                    prx.Proxy.AltEndpoints = ImmutableList.Create(prx2.Proxy.Endpoint!);
                    Assert.ThrowsAsync<RetrySystemFailure>(async () => await prx.OtherReplicaAsync());
                    Assert.AreEqual(servers[0].ToString(), calls[0]);
                    Assert.AreEqual(servers[1].ToString(), calls[1]);
                    Assert.AreEqual(2, calls.Count);

                    // The first replica fails with ServiceNotFoundException exception the second replica fails with
                    // ConnectionLostException the last failure should be reported. The 3rd replica cannot be used
                    // because ConnectionLostException cannot be retry for a non idempotent request.
                    calls.Clear();
                    prx = new RetryReplicatedTestPrx(prx1.Proxy.Clone());
                    prx.Proxy.AltEndpoints = ImmutableList.Create(prx3.Proxy.Endpoint!, prx2.Proxy.Endpoint!);

                    Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.OtherReplicaAsync());
                    Assert.AreEqual(servers[0].ToString(), calls[0]);
                    Assert.AreEqual(servers[2].ToString(), calls[1]);
                    Assert.AreEqual(2, calls.Count);

                    // The first replica fails with ServiceNotFoundException exception and there is no additional
                    // replicas.
                    calls.Clear();
                    Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx1.OtherReplicaAsync());
                    Assert.AreEqual(servers[0].ToString(), calls[0]);
                });
        }

        [Test]
        public async Task Retry_RetryBufferMaxSize()
        {
            await WithRetryServiceAsync(
                (pipeline, pool) => pipeline.UseRetry(new RetryOptions { MaxAttempts = 5, BufferMaxSize = 2048 })
                                            .UseBinder(pool),
                async (service, retry) =>
                {
                    byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
                    // Use two connections to simulate two concurrent requests, the first should succeed
                    // and the second should fail because the buffer size max.
                    await using var connection1 = new Connection { RemoteEndpoint = retry.Proxy.Endpoint };
                    await using var connection2 = new Connection { RemoteEndpoint = retry.Proxy.Endpoint };

                    await connection1.ConnectAsync();
                    await connection2.ConnectAsync();

                    var retry1 = new RetryTestPrx(retry.Proxy.Clone());
                    retry1.Proxy.Connection = connection1;
                    retry1.Proxy.Endpoint = null; // endpointless proxy

                    Task t1 = retry1.OpWithDataAsync(2, 5000, data);
                    await Task.Delay(1000); // Ensure the first request is sent before the second request

                    var retry2 = new RetryTestPrx(retry.Proxy.Clone());
                    retry2.Proxy.Connection = connection2;
                    retry2.Proxy.Endpoint = null; // endpointless proxy
                    Task t2 = retry2.OpWithDataAsync(2, 0, data);

                    Assert.DoesNotThrowAsync(async () => await t1);
                    Assert.ThrowsAsync<RetrySystemFailure>(async () => await t2);

                    await retry1.OpWithDataAsync(2, 100, data);
                });
        }

        [TestCase(1024, 1024)]
        [TestCase(1024, 2048)]
        public async Task Retry_RetryRequestSizeMax(int maxSize, int requestSize)
        {
            await WithRetryServiceAsync(
                (pipeline, pool) => pipeline.UseRetry(new RetryOptions { MaxAttempts = 5, RequestMaxSize = maxSize })
                                            .UseBinder(pool),
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

        private static Pipeline CreatePipeline(ConnectionPool pool)
        {
            var pipeline = new Pipeline();
            pipeline.UseLogger(LogAttributeLoggerFactory.Instance);
            pipeline.UseRetry(new RetryOptions { MaxAttempts = 5 });
            pipeline.UseBinder(pool);
            return pipeline;
        }

        private static ConnectionPool CreateConnectionPool()
        {
            var pool = new ConnectionPool()
            {
                ClientTransport = new ClientTransport().UseTcp(),
                ConnectionOptions = new() { CloseTimeout = TimeSpan.FromMinutes(5) },
                LoggerFactory = LogAttributeLoggerFactory.Instance
            };
            return pool;
        }

        private async Task WithReplicatedRetryServiceAsync(int replicas, Action<Server[], Router[]> closure)
        {
            Server[] servers = Enumerable.Range(0, replicas).Select(
                i => new Server
                {
                    Endpoint = GetTestEndpoint(port: i),
                    ServerTransport = new ServerTransport().UseTcp(),
                    ConnectionOptions = new() { CloseTimeout = TimeSpan.FromMinutes(5) },
                    LoggerFactory = LogAttributeLoggerFactory.Instance
                }).ToArray();

            Router[] routers = Enumerable.Range(0, replicas).Select(i => new Router()).ToArray();

            closure(servers, routers);
            await Task.WhenAll(servers.Select(server => server.ShutdownAsync()));
        }

        private async Task WithRetryServiceAsync(
            Protocol protocol,
            Action<Pipeline, IConnectionProvider>? configure,
            Func<RetryTest, RetryTestPrx, Task> closure)
        {
            await using ConnectionPool pool = CreateConnectionPool();
            Pipeline pipeline;
            if (configure != null)
            {
                pipeline = new Pipeline();
                configure(pipeline, pool);
            }
            else
            {
                pipeline = CreatePipeline(pool);
            }

            var router = new Router();
            var service = new RetryTest();
            router.UseLogger(LogAttributeLoggerFactory.Instance);
            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    service.Attempts++;
                    service.Connection = request.Connection;
                    return await next.DispatchAsync(request, cancel);
                }));
            router.Map("/retry", service);

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = GetTestEndpoint(protocol: protocol),
                ServerTransport = new ServerTransport().UseTcp(),
                ConnectionOptions = new() { CloseTimeout = TimeSpan.FromMinutes(5) },
                LoggerFactory = LogAttributeLoggerFactory.Instance
            };
            server.Listen();

            var retry = RetryTestPrx.Parse(GetTestProxy("/retry", protocol: protocol), pipeline);
            await closure(service, retry);
        }

        private Task WithRetryServiceAsync(Func<RetryTest, RetryTestPrx, Task> closure) =>
            WithRetryServiceAsync(Protocol.Ice2, null, closure);

        private Task WithRetryServiceAsync(
            Action<Pipeline, IConnectionProvider> configure,
            Func<RetryTest, RetryTestPrx, Task> closure) =>
            WithRetryServiceAsync(Protocol.Ice2, configure, closure);

        internal class RetryTest : Service, IRetryTest
        {
            internal volatile int Attempts;
            internal Connection? Connection;

            public async ValueTask OpIdempotentAsync(
                int failedAttempts,
                bool killConnection,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                if (Attempts <= failedAttempts)
                {
                    if (killConnection)
                    {
                        await dispatch.Connection.CloseAsync();
                    }
                    else
                    {
                        throw new RetrySystemFailure(RetryPolicy.Immediately);
                    }
                }
            }

            public async ValueTask OpNotIdempotentAsync(
                int failedAttempts,
                bool killConnection,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                if (Attempts <= failedAttempts)
                {
                    if (killConnection)
                    {
                        await dispatch.Connection.CloseAsync();
                    }
                    else
                    {
                        throw new RetrySystemFailure(RetryPolicy.Immediately);
                    }
                }
            }

            public async ValueTask OpWithDataAsync(
                int failedAttempts,
                int delay,
                byte[] data,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                if (failedAttempts >= Attempts)
                {
                    await Task.Delay(delay, cancel);
                    throw new RetrySystemFailure(RetryPolicy.Immediately);
                }
            }

            public ValueTask OpRetryAfterDelayAsync(
                int failedAttempts,
                int delay,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                if (failedAttempts >= Attempts)
                {
                    throw new RetrySystemFailure(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(delay)));
                }
                return default;
            }

            public ValueTask OpRetryNoAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new RetrySystemFailure(RetryPolicy.NoRetry);
        }
    }

    public class Bidir : Service, IRetryBidirTest
    {
        private int _n;

        public ValueTask AfterDelayAsync(int n, Dispatch dispatch, CancellationToken cancel)
        {
            if (++_n < n)
            {
                throw new ServiceNotFoundException(RetryPolicy.AfterDelay(TimeSpan.FromMilliseconds(10)));
            }
            _n = 0;
            return default;
        }

        public ValueTask OtherReplicaAsync(Dispatch dispatch, CancellationToken cancel) =>
            throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
    }

    public class Replicated : Service, IRetryReplicatedTest
    {
        private readonly bool _fail;
        public Replicated(bool fail) => _fail = fail;

        public ValueTask OtherReplicaAsync(Dispatch dispatch, CancellationToken cancel)
        {
            if (_fail)
            {
                throw new RetrySystemFailure(RetryPolicy.OtherReplica);
            }
            return default;
        }
    }
}
