// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public class RetryTests
    {
        [Test]
        public async Task Retry_AfterDelay()
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

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
            Assert.That(service.Attempts, Is.EqualTo(2));
        }

        [Test]
        public async Task Retry_ConnectionEstablishment()
        {
            var colocTranport = new ColocTransport();
            await using ServiceProvider serviceProvider1 = new RetryIntegrationTestServiceCollection()
                .UseColoc(colocTranport, 0)
                .BuildServiceProvider();

            await using ServiceProvider serviceProvider2 = new RetryIntegrationTestServiceCollection()
                .UseColoc(colocTranport, 1)
                .BuildServiceProvider();

            await using ServiceProvider serviceProvider3 = new RetryIntegrationTestServiceCollection()
                .UseColoc(colocTranport, 20)
                .BuildServiceProvider();

            Server server1 = serviceProvider1.GetRequiredService<Server>();
            Server server2 = serviceProvider2.GetRequiredService<Server>();
            Server server3 = serviceProvider3.GetRequiredService<Server>();

            var proxy = Proxy.Parse($"{server1.Endpoint}", serviceProvider1.GetRequiredService<IInvoker>());
            proxy = proxy with { Path = "/retry" };

            var prx = new RetryReplicatedTestPrx(proxy);

            prx.Proxy.AltEndpoints = ImmutableList.Create(server2.Endpoint, server3.Endpoint);

            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(
                prx.Proxy.Connection!.NetworkConnectionInformation!.Value.RemoteEndpoint, Is.EqualTo(server1.Endpoint));
            await server1.ShutdownAsync();
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(
                prx.Proxy.Connection!.NetworkConnectionInformation!.Value.RemoteEndpoint, Is.EqualTo(server2.Endpoint));
            await server2.ShutdownAsync();
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.That(
                prx.Proxy.Connection!.NetworkConnectionInformation!.Value.RemoteEndpoint, Is.EqualTo(server3.Endpoint));
        }

        [Test]
        public async Task Retry_EndpointlessProxy()
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Bidir>()
                .BuildServiceProvider();

            var retryBidir = RetryBidirTestPrx.Parse("icerpc:/retry");
            retryBidir.Proxy.Endpoint = serviceProvider.GetRequiredService<Server>().Endpoint;
            retryBidir.Proxy.Invoker = serviceProvider.GetRequiredService<IInvoker>();
            await retryBidir.IcePingAsync();

            // endpointless proxy with a connection
            var bidir = new RetryBidirTestPrx(retryBidir.Proxy with { Endpoint = null });

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await bidir.OtherReplicaAsync(cancel: CancellationToken.None));

            // The exception is not retryable with the Ice protocol. With the IceRPC protocol, we can retry using the
            // existing connection because the exception uses the AfterDelay retry policy.
            Assert.That(bidir.Proxy.Protocol, Is.EqualTo(Protocol.IceRpc));
            await bidir.AfterDelayAsync(2);
        }

        [TestCase("ice", 2)]
        [TestCase("ice", 10)]
        [TestCase("ice", 20)]
        [TestCase("icerpc", 2)]
        [TestCase("icerpc", 10)]
        [TestCase("icerpc", 20)]
        public async Task Retry_GracefulClose(string protocol, int maxQueue)
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

            // Remote case: send multiple OpWithData, followed by a close and followed by multiple OpWithData. The
            // goal is to make sure that none of the OpWithData fail even if the server closes the connection
            // gracefully in between.
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
        }

        [TestCase("ice", 2)]
        [TestCase("ice", 10)]
        [TestCase("ice", 20)]
        [TestCase("icerpc", 2)]
        [TestCase("icerpc", 10)]
        [TestCase("icerpc", 20)]
        public async Task Retry_GracefulCloseCanceled(string protocol, int maxQueue)
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

            // Remote case: send multiple OpWithData, followed by a close and followed by multiple OpWithData. The goal
            // is to make sure that none of the OpWithData fail even if the server closes the connection gracefully in
            // between.
            byte[] seq = new byte[1024 * 10];

            await retry.IcePingAsync();
            var results = new List<Task>();
            for (int i = 0; i < maxQueue; ++i)
            {
                results.Add(retry.OpWithDataAsync(-1, 0, seq));
            }

            using var source = new CancellationTokenSource();
            Task shutdownTask = service.Connection!.ShutdownAsync(cancel: source.Token);
            source.Cancel();

            for (int i = 0; i < maxQueue; i++)
            {
                results.Add(retry.OpWithDataAsync(-1, 0, seq));
            }

            if (protocol == "icerpc")
            {
                // With icerpc cancelation, the peer shutdown might cancel invocations which are being dispatched.
                try
                {
                    await Task.WhenAll(results);
                }
                catch (OperationCanceledException)
                {
                }
                catch (AggregateException ex)
                {
                    Assert.That(ex.InnerExceptions.All(exception => exception is OperationCanceledException), Is.True);
                }
            }
            else
            {
                await Task.WhenAll(results);
            }
        }

        [TestCase("icerpc", 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase("icerpc", 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase("icerpc", 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase("icerpc", 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase("icerpc", 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase("icerpc", 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase("icerpc", 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection

        [TestCase("ice", 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase("ice", 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase("ice", 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase("ice", 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase("ice", 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase("ice", 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase("ice", 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        public async Task Retry_Idempotent(
            string protocol,
            int failedAttempts,
            int maxAttempts,
            bool killConnection)
        {
            Assert.That(failedAttempts, Is.GreaterThan(0));

            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .AddTransient(_ => new RetryOptions { MaxAttempts = maxAttempts })
                .UseProtocol(protocol)
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

            // Idempotent operations can always be retried, the operation must succeed if the failed attempts
            // are less than the invocation max attempts configured above.
            // With the Ice protocol, user exceptions don't carry a retry policy and are not retryable
            if (failedAttempts < maxAttempts && (protocol == "icerpc" || killConnection))
            {
                await retry.OpIdempotentAsync(failedAttempts, killConnection);
                Assert.That(service.Attempts, Is.EqualTo(failedAttempts + 1));
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

                if (protocol == "icerpc" || killConnection)
                {
                    Assert.That(service.Attempts, Is.EqualTo(maxAttempts));
                }
                else
                {
                    Assert.That(service.Attempts, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public async Task Retry_No()
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

            // There is a single attempt because the retry policy doesn't allow to retry
            Assert.CatchAsync<RetrySystemFailure>(async () => await retry.OpRetryNoAsync());
            Assert.That(service.Attempts, Is.EqualTo(1));
        }

        [TestCase("icerpc", 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase("icerpc", 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase("icerpc", 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase("icerpc", 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase("icerpc", 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase("icerpc", 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase("icerpc", 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        [TestCase("icerpc", 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection

        [TestCase("ice", 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        [TestCase("ice", 1, 1, true)]  // 1 failure, 1 max attempts, kill the connection
        [TestCase("ice", 1, 3, false)] // 1 failure, 3 max attempts, don't kill the connection
        [TestCase("ice", 1, 3, true)]  // 1 failure, 3 max attempts, kill the connection
        [TestCase("ice", 5, 5, false)] // 5 failures, 5 max attempts, don't kill the connection
        [TestCase("ice", 5, 5, true)]  // 5 failures, 5 max attempts, kill the connection
        [TestCase("ice", 4, 5, false)] // 4 failures, 5 max attempts, don't kill the connection
        [TestCase("ice", 1, 1, false)] // 1 failure, 1 max attempts, don't kill the connection
        public async Task Retry_NoIdempotent(
            string protocol,
            int failedAttempts,
            int maxAttempts,
            bool killConnection)
        {
            Assert.That(failedAttempts, Is.GreaterThan(0));

            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .AddTransient(_ => new RetryOptions { MaxAttempts = maxAttempts })
                .UseProtocol(protocol)
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

            if (failedAttempts > 0 && killConnection)
            {
                // Connection failures after a request was sent are not retryable for non idempotent operations
                Assert.CatchAsync<ConnectionLostException>(
                    async () => await retry.OpNotIdempotentAsync(failedAttempts, killConnection));
                Assert.That(service.Attempts, Is.EqualTo(1));
            }
            else if (failedAttempts < maxAttempts && (protocol == "icerpc" || killConnection))
            {
                await retry.OpNotIdempotentAsync(failedAttempts, killConnection);
                Assert.That(service.Attempts, Is.EqualTo(failedAttempts + 1));
            }
            else
            {
                Assert.CatchAsync<RetrySystemFailure>(
                    async () => await retry.OpNotIdempotentAsync(failedAttempts, killConnection));
                if (protocol == "icerpc" || killConnection)
                {
                    Assert.That(service.Attempts, Is.EqualTo(maxAttempts));
                }
                else
                {
                    Assert.That(service.Attempts, Is.EqualTo(1));
                }
            }
        }

        [Test]
        // [Log(LogAttributeLevel.Information)]
        public async Task Retry_OtherReplica()
        {
            var calls = new List<string>();

            var colocTransport = new ColocTransport();

            await using ServiceProvider serviceProvider1 = new RetryIntegrationTestServiceCollection()
                .UseColoc(colocTransport, 1)
                .AddTransient(_ => CreateDispatcher(new Replicated(fail: true)))
                .BuildServiceProvider();

            await using ServiceProvider serviceProvider2 = new RetryIntegrationTestServiceCollection()
                .UseColoc(colocTransport, 2)
                .AddTransient(_ => CreateDispatcher(new Replicated(fail: false)))
                .BuildServiceProvider();

            Server server1 = serviceProvider1.GetRequiredService<Server>();
            Server server2 = serviceProvider2.GetRequiredService<Server>();

            var prx = new RetryReplicatedTestPrx(
                Proxy.Parse($"{server1.Endpoint}",
                serviceProvider1.GetRequiredService<IInvoker>()));
            prx.Proxy.AltEndpoints = ImmutableList.Create(server2.Endpoint);

            // The service proxy has 2 endpoints, the request fails using the first endpoint with a retryable
            // exception that has OtherReplica retry policy, it then retries the second endpoint and succeed.
            Assert.DoesNotThrowAsync(async () => await prx.OtherReplicaAsync());
            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(server1.ToString(), Is.EqualTo(calls[0]));
            Assert.That(server2.ToString(), Is.EqualTo(calls[1]));

            IDispatcher CreateDispatcher(IDispatcher next)
            {
                return new InlineDispatcher(
                    async (request, cancel) =>
                    {
                        calls.Add(request.Connection.NetworkConnectionInformation!.Value.LocalEndpoint.ToString());
                        return await next.DispatchAsync(request, cancel);
                    });
            }
        }

        [Test]
        public async Task Retry_ReportLastFailure()
        {
            var calls = new List<string>();

            var colocTransport = new ColocTransport();

            await using ServiceProvider serviceProvider1 = new IntegrationTestServiceCollection()
                .UseColoc(colocTransport, 1)
                .AddTransient(_ => CreateDispatcher(new InlineDispatcher((request, cancel) =>
                    throw new ServiceNotFoundException(RetryPolicy.OtherReplica))))
                .BuildServiceProvider();

            await using ServiceProvider serviceProvider2 = new IntegrationTestServiceCollection()
                .UseColoc(colocTransport, 2)
                .AddTransient(_ => CreateDispatcher(new Replicated(fail: true)))
                .BuildServiceProvider();

            await using ServiceProvider serviceProvider3 = new IntegrationTestServiceCollection()
                .UseColoc(colocTransport, 3)
                .AddTransient(_ => CreateDispatcher(new InlineDispatcher(async (request, cancel) =>
                {
                    await request.Connection.CloseAsync("forcefully close connection!");
                    await Task.Delay(1000, cancel);
                    throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
                })))
                .BuildServiceProvider();

            Server server1 = serviceProvider1.GetRequiredService<Server>();
            Server server2 = serviceProvider2.GetRequiredService<Server>();
            Server server3 = serviceProvider3.GetRequiredService<Server>();

            ConnectionPool pool = serviceProvider1.GetRequiredService<ConnectionPool>();
            pool.PreferExistingConnection = false;
            var pipeline = new Pipeline();
            pipeline.UseRetry(new RetryOptions
            {
                MaxAttempts = 5,
                LoggerFactory = LogAttributeLoggerFactory.Instance
            });
            pipeline.UseBinder(pool, cacheConnection: false);
            var prx = RetryReplicatedTestPrx.Parse($"{server1.Endpoint}", pipeline);

            // The first replica fails with ServiceNotFoundException exception and there is no additional replicas.
            calls.Clear();
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OtherReplicaAsync());
            Assert.That(calls.Count, Is.EqualTo(1));
            Assert.That(server1.ToString(), Is.EqualTo(calls[0]));

            // The first replica fails with ServiceNotFoundException exception the second replica fails with
            // RetrySystemFailure the last failure should be reported.
            calls.Clear();
            prx.Proxy.AltEndpoints = ImmutableList.Create(server2.Endpoint);
            Assert.ThrowsAsync<RetrySystemFailure>(async () => await prx.OtherReplicaAsync());
            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(server1.ToString(), Is.EqualTo(calls[0]));
            Assert.That(server2.ToString(), Is.EqualTo(calls[1]));


            // The first replica fails with ServiceNotFoundException exception the second replica fails with
            // ConnectionLostException the last failure should be reported. The 3rd replica cannot be used because
            // ConnectionLostException cannot be retried for a non idempotent request.
            calls.Clear();
            prx.Proxy.AltEndpoints = ImmutableList.Create(server3.Endpoint, server2.Endpoint);
            Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.OtherReplicaAsync());
            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(server1.ToString(), Is.EqualTo(calls[0]));
            Assert.That(server3.ToString(), Is.EqualTo(calls[1]));

            IDispatcher CreateDispatcher(IDispatcher next)
            {
                return new InlineDispatcher(
                    async (request, cancel) =>
                    {
                        calls.Add(request.Connection.NetworkConnectionInformation!.Value.LocalEndpoint.ToString());
                        return await next.DispatchAsync(request, cancel);
                    });
            }
        }

        /*
        // TODO: reenable once we implement again buffer max size
        [Test]
        public async Task Retry_RetryBufferMaxSize()
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .UseTransport("tcp")
                .AddTransient(_ => new RetryOptions { MaxAttempts = 5, BufferMaxSize = 2048 })
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

            byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
            // Use two connections to simulate two concurrent requests, the first should succeed
            // and the second should fail because the buffer size max.
            await using var connection1 = new Connection { RemoteEndpoint = retry.Proxy.Endpoint! };
            await using var connection2 = new Connection { RemoteEndpoint = retry.Proxy.Endpoint! };

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
        }

        /*
        TODO: reenable once RetryInterceptor supports max size again.

        [TestCase(1024, 1024)]
        [TestCase(1024, 2048)]
        public async Task Retry_RetryRequestSizeMax(int maxSize, int requestSize)
        {
            await using ServiceProvider serviceProvider = new RetryIntegrationTestServiceCollection()
                .AddTransient(_ => new RetryOptions { MaxAttempts = 5, RequestMaxSize = maxSize })
                .BuildServiceProvider();
            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
            RetryTestPrx retry = new(serviceProvider.GetRequiredService<Proxy>());

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
        }
        */

        private class RetryIntegrationTestServiceCollection : IntegrationTestServiceCollection
        {
            internal RetryIntegrationTestServiceCollection()
            {
                this.AddScoped<IDispatcher>(serviceProvider =>
                {
                    var router = new Router();
                    router.UseLogger(LogAttributeLoggerFactory.Instance);
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            RetryTest service = serviceProvider.GetRequiredService<RetryTest>();
                            service.Attempts++;
                            service.Connection = request.Connection;
                            return await next.DispatchAsync(request, cancel);
                        }));
                    router.Map("/retry", serviceProvider.GetRequiredService<RetryTest>());
                    return router;
                });
                this.AddScoped(_ => new RetryOptions { MaxAttempts = 5 });
                this.AddScoped(_ => new RetryTest());
                this.AddScoped<IInvoker>(serviceProvider =>
                {
                    var pipeline = new Pipeline();
                    pipeline.UseLogger(LogAttributeLoggerFactory.Instance);
                    pipeline.UseRetry(serviceProvider.GetRequiredService<RetryOptions>());
                    pipeline.UseBinder(serviceProvider.GetRequiredService<ConnectionPool>());
                    return pipeline;
                });
                this.AddScoped(serviceProvider =>
                {
                    Endpoint serverEndpoint = serviceProvider.GetRequiredService<Server>().Endpoint;
                    var proxy = Proxy.Parse($"{serverEndpoint.Protocol}:/retry");
                    proxy.Endpoint = serverEndpoint;
                    proxy.Invoker = serviceProvider.GetRequiredService<IInvoker>();
                    return proxy;
                });
            }
        }

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
