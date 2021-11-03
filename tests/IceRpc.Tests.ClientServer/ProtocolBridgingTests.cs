// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.Collections.Immutable;

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
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(_forwarderServer.DisposeAsync().AsTask(),
                               _targetServer.DisposeAsync().AsTask());
            await _pool.DisposeAsync();
        }

        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice2, true)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice2, false)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice2, true)]
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice2, false)]
        public async Task ProtocolBridging_Forward(ProtocolCode forwarderProtocol, ProtocolCode targetProtocol, bool colocated)
        {
            // TODO: add context testing

            var pipeline = new Pipeline();
            pipeline.UseBinder(_pool);

            (ProtocolBridgingTestPrx forwarderServicePrx, ProtocolBridgingTest targetService) = SetupForwarderServer(
                Protocol.FromProtocolCode(forwarderProtocol),
                Protocol.FromProtocolCode(targetProtocol),
                colocated,
                pipeline);

            // TODO: test with the other encoding; currently, the encoding is always the encoding of
            // forwardService.Proxy.Protocol

            ProtocolBridgingTestPrx newPrx = await TestProxyAsync(forwarderServicePrx, direct: false);


            if (colocated)
            {
                if (newPrx.Proxy.Connection == null)
                {
                    Assert.That(newPrx.Proxy.Endpoint, Is.Null);
                    Assert.AreEqual(Protocol.Ice1, newPrx.Proxy.Protocol);

                    // Fix up the "well-known" proxy
                    // TODO: cleaner solution?
                    newPrx.Proxy.Endpoint = _targetServer.Endpoint;
                }
            }
            else
            {
                Assert.AreEqual(targetProtocol, newPrx.Proxy.Protocol.Code);
            }

            _ = await TestProxyAsync(newPrx, direct: true);

            async Task<ProtocolBridgingTestPrx> TestProxyAsync(ProtocolBridgingTestPrx prx, bool direct)
            {
                Assert.AreEqual(prx.Proxy.Path, direct ? "/target" : "/forward");

                Assert.AreEqual(13, await prx.OpAsync(13));

                var invocation = new Invocation
                {
                    Context = new Dictionary<string, string> { ["MyCtx"] = "hello" }
                };
                await prx.OpContextAsync(invocation);
                CollectionAssert.AreEqual(invocation.Context, targetService.Context);
                targetService.Context = ImmutableDictionary<string, string>.Empty;

                await prx.OpVoidAsync();

                await prx.OpOnewayAsync(42);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());

                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OpServiceNotFoundExceptionAsync());

                ProtocolBridgingTestPrx newProxy = await prx.OpNewProxyAsync();
                return newProxy;
            }
        }

        private (ProtocolBridgingTestPrx, ProtocolBridgingTest) SetupForwarderServer(
            Protocol forwarderProtocol,
            Protocol targetProtocol,
            bool colocated,
            IInvoker invoker)
        {
            var targetService = new ProtocolBridgingTest();
            _targetServer = CreateServer(targetProtocol, port: 0, colocated);
            _router.Map("/target", targetService);
            _targetServer.Dispatcher = _router;
            _targetServer.Listen();
            var targetServicePrx = ProtocolBridgingTestPrx.FromPath("/target", targetProtocol);
            targetServicePrx.Proxy.Endpoint = _targetServer.Endpoint;
            targetServicePrx.Proxy.Invoker = invoker;

            _forwarderServer = CreateServer(forwarderProtocol, port: 1, colocated);
            _router.Map("/forward", new Forwarder(targetServicePrx.Proxy));
            _forwarderServer.Dispatcher = _router;
            _forwarderServer.Listen();
            var forwardServicePrx = ProtocolBridgingTestPrx.FromPath("/forward", forwarderProtocol);
            forwardServicePrx.Proxy.Endpoint = _forwarderServer.Endpoint;
            forwardServicePrx.Proxy.Invoker = invoker;
            return (forwardServicePrx, targetService);

            Server CreateServer(Protocol protocol, int port, bool colocated) => new()
            {
                Endpoint = colocated ?
                        TestHelper.GetUniqueColocEndpoint(protocol) :
                        GetTestEndpoint(port: port, protocol: protocol)
            };
        }

        internal class ProtocolBridgingTest : Service, IProtocolBridgingTest
        {
            public ImmutableDictionary<string, string> Context { get; set; } = ImmutableDictionary<string, string>.Empty;

            public ValueTask<int> OpAsync(int x, Dispatch dispatch, CancellationToken cancel) =>                
                new(x);

            public ValueTask OpContextAsync(Dispatch dispatch, CancellationToken cancel)
            {
                Context = dispatch.Context.ToImmutableDictionary();
                return default;
            }
            public ValueTask OpExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ProtocolBridgingException(42);

            public ValueTask<ProtocolBridgingTestPrx> OpNewProxyAsync(Dispatch dispatch, CancellationToken cancel)
            {
                var proxy = Proxy.FromPath(dispatch.Path, dispatch.Protocol);
                proxy.Endpoint = dispatch.Connection.NetworkConnectionInformation?.LocalEndpoint;
                proxy.Encoding = dispatch.Encoding; // use the request's encoding instead of the server's encoding.
                return new(new ProtocolBridgingTestPrx(proxy));
            }

            public ValueTask OpOnewayAsync(int x, Dispatch dispatch, CancellationToken cancel) => default;

            public ValueTask OpServiceNotFoundExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ServiceNotFoundException();

            public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        public sealed class Forwarder : IDispatcher
        {
            private readonly Proxy _target;

            async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(
                IncomingRequest incomingRequest,
                CancellationToken cancel)
            {
                var outgoingRequest = incomingRequest.ToOutgoingRequest(_target);
                IncomingResponse incomingResponse = await _target.Invoker!.InvokeAsync(outgoingRequest, cancel);
                return incomingResponse.ToOutgoingResponse(incomingRequest.Protocol);
            }

            internal Forwarder(Proxy target) => _target = target;
        }
    }
}
