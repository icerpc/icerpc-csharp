// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public sealed class ProtocolBridgingTests : ClientServerBaseTest
    {
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice2, true)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice2, false)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice2, true)]
        [TestCase(ProtocolCode.Ice2, ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice1, ProtocolCode.Ice2, false)]
        public async Task ProtocolBridging_Forward(
            ProtocolCode forwarderProtocolCode,
            ProtocolCode targetProtocolCode,
            bool colocated)
        {
            var targetProtocol = Protocol.FromProtocolCode(targetProtocolCode);
            var forwarderProtocol = Protocol.FromProtocolCode(forwarderProtocolCode);

            // TODO: add context testing
            await using var pool = new ConnectionPool();

            var pipeline = new Pipeline();
            pipeline.UseBinder(pool);

            var router = new Router();

            await using var targetServer = new Server
            {
                Endpoint = colocated ?
                    TestHelper.GetUniqueColocEndpoint(targetProtocol) :
                    GetTestEndpoint(port: 0, protocol: targetProtocol),
                Dispatcher = router
            };
            targetServer.Listen();

            await using var forwarderServer = new Server
            {
                Endpoint = colocated ?
                    TestHelper.GetUniqueColocEndpoint(forwarderProtocol) :
                    GetTestEndpoint(port: 1, protocol: forwarderProtocol),
                Dispatcher = router
            };
            forwarderServer.Listen();

            var targetServicePrx = ProtocolBridgingTestPrx.FromPath("/target", targetProtocol);
            targetServicePrx.Proxy.Endpoint = targetServer.Endpoint;
            targetServicePrx.Proxy.Invoker = pipeline;

            var forwarderServicePrx = ProtocolBridgingTestPrx.FromPath("/forward", forwarderProtocol);
            forwarderServicePrx.Proxy.Endpoint = forwarderServer.Endpoint;
            forwarderServicePrx.Proxy.Invoker = pipeline;

            var targetService = new ProtocolBridgingTest();
            router.Map("/target", targetService);
            router.Map("/forward", new Forwarder(targetServicePrx.Proxy));

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
                    newPrx.Proxy.Endpoint = targetServer.Endpoint;
                }
            }
            else
            {
                Assert.AreEqual(targetProtocol, newPrx.Proxy.Protocol);
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

                ServiceNotFoundException exception = Assert.ThrowsAsync<ServiceNotFoundException>(
                    async () => await prx.OpServiceNotFoundExceptionAsync());

                // Verifies the exception is correctly populated:
                Assert.AreEqual("/target", exception.Origin.Path);
                Assert.AreEqual("opServiceNotFoundException", exception.Origin.Operation);

                ProtocolBridgingTestPrx newProxy = await prx.OpNewProxyAsync();
                return newProxy;
            }
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
