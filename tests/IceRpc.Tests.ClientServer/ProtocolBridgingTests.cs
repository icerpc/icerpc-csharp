// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class ProtocolBridgingTests : ClientServerBaseTest
    {
        private readonly Communicator _communicator;
        private Server _forwarderServer = null!;
        private readonly Router _router = new(); // shared by both servers for coloc to work properly
        private Server _targetServer = null!;

        public ProtocolBridgingTests() => _communicator = new Communicator();

        [TearDown]
        public async Task TearDownAsync()
        {
            await Task.WhenAll(_forwarderServer.ShutdownAsync(), _targetServer.ShutdownAsync());
            await _communicator.DisposeAsync();
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

            IProtocolBridgingServicePrx forwarderService =
                SetupForwarderServer(forwarderProtocol, targetProtocol, colocated);

            var newPrx = await TestProxyAsync(forwarderService, direct: false);

            // When colocated, the proxy is a relative proxy and inherits the forwarder's proxy protocol.
            Assert.AreEqual(colocated ? forwarderProtocol : targetProtocol, newPrx.Protocol);

            _ = await TestProxyAsync(newPrx, direct: true);

            async Task<IProtocolBridgingServicePrx> TestProxyAsync(IProtocolBridgingServicePrx prx, bool direct)
            {
                Assert.AreEqual(prx.Path, direct ? "/target" : "/forward");

                var ctx = new Dictionary<string, string>(prx.Context)
                {
                    { "MyCtx", "hello" }
                };

                Assert.AreEqual(13, await prx.OpAsync(13, ctx));

                await prx.OpVoidAsync(ctx);

                (int v, string s) = await prx.OpReturnOutAsync(34, ctx);
                Assert.AreEqual(34, v);
                Assert.AreEqual("value=34", s);

                await prx.OpOnewayAsync(42);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OpServiceNotFoundExceptionAsync());

                prx = prx.OpNewProxy();
                prx.Context = new Dictionary<string, string> { { "Direct", "1" } };
                return prx;
            }
        }

        private IProtocolBridgingServicePrx SetupForwarderServer(
            Protocol forwarderProtocol,
            Protocol targetProtocol,
            bool colocated)
        {
            _targetServer = CreateServer(targetProtocol, port: 0, colocated);

            _router.Map("/target", new ProtocolBridgingService());
            var targetService = CreateProxy<IProtocolBridgingServicePrx>(_targetServer, "/target");
            _targetServer.Dispatcher = _router;
            _ = _targetServer.ListenAndServeAsync();

            _forwarderServer = CreateServer(forwarderProtocol, port: 1, colocated);
            _router.Map("/forward", new Forwarder(targetService));
            var forwardService = CreateProxy<IProtocolBridgingServicePrx>(_forwarderServer, "/forward");
            _forwarderServer.Dispatcher = _router;
            _ = _forwarderServer.ListenAndServeAsync();
            return forwardService;

            Server CreateServer(Protocol protocol, int port, bool colocated) =>
                new Server
                {
                    Communicator = _communicator,
                    ColocationScope = colocated ? ColocationScope.Communicator : ColocationScope.None,
                    Endpoint = colocated ? "" : GetTestEndpoint(port: port, protocol: protocol),
                    Protocol = protocol
                };
        }

        internal class ProtocolBridgingService : IAsyncProtocolBridgingService
        {
            public ValueTask<int> OpAsync(int x, Current current, CancellationToken cancel) =>
                new(x);

            public ValueTask OpExceptionAsync(Current current, CancellationToken cancel) =>
                throw new ProtocolBridgingException(42);

            public ValueTask<IProtocolBridgingServicePrx> OpNewProxyAsync(Current current, CancellationToken cancel)
            {
                var proxy = CreateProxy<IProtocolBridgingServicePrx>(current.Server, current.Path);
                proxy.Encoding = current.Encoding; // use the request's encoding instead of the server's encoding.
                return new(proxy);
            }

            public ValueTask OpOnewayAsync(int x, Current current, CancellationToken cancel) => default;

            public ValueTask<(int ReturnValue, string Y)> OpReturnOutAsync(
                int x,
                Current current,
                CancellationToken cancel) =>
                new((x, $"value={x}"));

            public ValueTask OpServiceNotFoundExceptionAsync(Current current, CancellationToken cancel) =>
                throw new ServiceNotFoundException();

            public ValueTask OpVoidAsync(Current current, CancellationToken cancel) => default;
        }

        public sealed class Forwarder : IService
        {
            private readonly IServicePrx _target;

            ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel)
            {
                current.Context["Forwarded"] = "1";
                return _target.ForwardAsync(current.IncomingRequestFrame, current.IsOneway, cancel: cancel);
            }

            internal Forwarder(IServicePrx target) => _target = target;
        }

        private static T CreateProxy<T>(Server server, string path) where T : class, IServicePrx =>
            server.Endpoint.Length == 0 ? server.CreateRelativeProxy<T>(path) : server.CreateProxy<T>(path);
    }
}
