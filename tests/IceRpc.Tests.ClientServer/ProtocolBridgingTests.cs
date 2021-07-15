// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public sealed class ProtocolBridgingTests : ClientServerBaseTest, IAsyncDisposable
    {
        private readonly ConnectionPool _pool;
        private Server _forwarderServer = null!;
        private readonly Router _router = new(); // shared by both servers for coloc to work properly
        private Server _targetServer = null!;

        public ProtocolBridgingTests()
        {
            _pool = new ConnectionPool();
            _pool.ConnectionOptions = new ClientConnectionOptions()
            {
                ClassFactory = new ClassFactory(
                    new Assembly[]
                    {
                        typeof(RemoteException).Assembly,
                        typeof(ProtocolBridgingException).Assembly
                    })
            };
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(_forwarderServer.DisposeAsync().AsTask(),
                               _targetServer.DisposeAsync().AsTask());
            await _pool.DisposeAsync();
        }

        [TestCase(Protocol.Ice2, Protocol.Ice2, true)]
        [TestCase(Protocol.Ice1, Protocol.Ice1, true)]
        [TestCase(Protocol.Ice2, Protocol.Ice2, false)]
        [TestCase(Protocol.Ice1, Protocol.Ice1, false)]
        [TestCase(Protocol.Ice2, Protocol.Ice1, true)]
        [TestCase(Protocol.Ice1, Protocol.Ice2, true)]
        [TestCase(Protocol.Ice2, Protocol.Ice1, false)]
        [TestCase(Protocol.Ice1, Protocol.Ice2, false)]
        public async Task ProtocolBridging_Forward(Protocol forwarderProtocol, Protocol targetProtocol, bool colocated)
        {
            // TODO: add context testing

            var pipeline = new Pipeline();
            pipeline.Use(Interceptors.Binder(_pool));

            IProtocolBridgingTestPrx forwarderService =
                SetupForwarderServer(forwarderProtocol, targetProtocol, colocated, pipeline);

            IProtocolBridgingTestPrx newPrx = await TestProxyAsync(forwarderService, direct: false);

            if (colocated)
            {
                if (newPrx.Connection == null)
                {
                    Assert.That(newPrx.Endpoint, Is.Null);
                    Assert.AreEqual(Protocol.Ice1, newPrx.Protocol);

                    // Fix up the "well-known" proxy
                    // TODO: cleaner solution?
                    newPrx.Endpoint = _targetServer.ProxyEndpoint;
                }
            }
            else
            {
                Assert.AreEqual(targetProtocol, newPrx.Protocol);
            }

            _ = await TestProxyAsync(newPrx, direct: true);

            async Task<IProtocolBridgingTestPrx> TestProxyAsync(IProtocolBridgingTestPrx prx, bool direct)
            {
                Assert.AreEqual(prx.Path, direct ? "/target" : "/forward");

                var invocation = new Invocation
                {
                    Context = new Dictionary<string, string> { ["MyCtx"] = "hello" }
                };

                Assert.AreEqual(13, await prx.OpAsync(13, invocation));

                await prx.OpVoidAsync(invocation);

                (int v, string s) = await prx.OpReturnOutAsync(34, invocation);
                Assert.AreEqual(34, v);
                Assert.AreEqual("value=34", s);

                await prx.OpOnewayAsync(42);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OpServiceNotFoundExceptionAsync());

                return await prx.OpNewProxyAsync();
            }
        }

        private IProtocolBridgingTestPrx SetupForwarderServer(
            Protocol forwarderProtocol,
            Protocol targetProtocol,
            bool colocated,
            IInvoker invoker)
        {
            _targetServer = CreateServer(targetProtocol, port: 0, colocated);
            _router.Map("/target", new ProtocolBridgingTest());
            _targetServer.Dispatcher = _router;
            _targetServer.Listen();
            var targetService = IProtocolBridgingTestPrx.FromServer(_targetServer, "/target");
            targetService.Invoker = invoker;

            _forwarderServer = CreateServer(forwarderProtocol, port: 1, colocated);
            _router.Map("/forward", new Forwarder(targetService));
            _forwarderServer.Dispatcher = _router;
            _forwarderServer.Listen();
            var forwardService = IProtocolBridgingTestPrx.FromServer(_forwarderServer, "/forward");
            forwardService.Invoker = invoker;
            return forwardService;

            Server CreateServer(Protocol protocol, int port, bool colocated) => new()
            {
                Endpoint = colocated ?
                        TestHelper.GetUniqueColocEndpoint(protocol) :
                        GetTestEndpoint(port: port, protocol: protocol),
                HostName = "127.0.0.1"
            };
        }

        internal class ProtocolBridgingTest : Service, IProtocolBridgingTest
        {
            public ValueTask<int> OpAsync(int x, Dispatch dispatch, CancellationToken cancel) =>
                new(x);

            public ValueTask OpExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ProtocolBridgingException(42);

            public ValueTask<IProtocolBridgingTestPrx> OpNewProxyAsync(Dispatch dispatch, CancellationToken cancel)
            {
                var proxy = IProtocolBridgingTestPrx.FromServer(dispatch.Server!, dispatch.Path);
                proxy.Encoding = dispatch.Encoding; // use the request's encoding instead of the server's encoding.
                return new(proxy);
            }

            public ValueTask OpOnewayAsync(int x, Dispatch dispatch, CancellationToken cancel) => default;

            public ValueTask<(int ReturnValue, string Y)> OpReturnOutAsync(
                int x,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((x, $"value={x}"));

            public ValueTask OpServiceNotFoundExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ServiceNotFoundException();

            public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        public sealed class Forwarder : IDispatcher
        {
            private readonly IServicePrx _target;

            async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(
                IncomingRequest incomingRequest,
                CancellationToken cancel)
            {
                IncomingResponse incomingResponse =
                    await _target.Invoker!.InvokeAsync(new OutgoingRequest(_target, incomingRequest), cancel);

                return new OutgoingResponse(incomingRequest, incomingResponse);
            }

            internal Forwarder(IServicePrx target) => _target = target;
        }
    }
}
