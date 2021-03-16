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
        private Server _targetServer = null!;
        private SortedDictionary<string, string>? _forwardedContext;

        public ProtocolBridgingTests()
        {
            _communicator = new Communicator();
            _forwarderServer = null!;
            _targetServer = null!;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await Task.WhenAll(_forwarderServer.ShutdownAsync(), _targetServer.ShutdownAsync());
            await _communicator.DisposeAsync();
        }

        [TestCase(Protocol.Ice2, Protocol.Ice2, true)]
        [TestCase(Protocol.Ice2, Protocol.Ice1, true)]
        // TODO enable once we fix https://github.com/zeroc-ice/icerpc-csharp/issues/140
        // [TestCase(Protocol.Ice1, Protocol.Ice2, true)]
        [TestCase(Protocol.Ice1, Protocol.Ice1, true)]
        [TestCase(Protocol.Ice2, Protocol.Ice2, false)]
        [TestCase(Protocol.Ice2, Protocol.Ice1, false)]
        [TestCase(Protocol.Ice1, Protocol.Ice2, false)]
        [TestCase(Protocol.Ice1, Protocol.Ice1, false)]
        public async Task ProtocolBridging_Forward(Protocol forwarderProtocol, Protocol targetProtocol, bool colocated)
        {
            IProtocolBridgingServicePrx forwarderService =
                SetupForwarderServer(forwarderProtocol, targetProtocol, colocated);

            var newPrx = await TestProxyAsync(forwarderService, false);
            Assert.AreEqual(newPrx.Protocol, targetProtocol);
            _ = await TestProxyAsync(newPrx, true);

            static void CheckContext(SortedDictionary<string, string> ctx, bool direct)
            {
                Assert.AreEqual(2, ctx.Count);
                Assert.AreEqual("hello", ctx["MyCtx"]);
                if (direct)
                {
                    Assert.AreEqual("1", ctx["Direct"]);
                }
                else
                {
                    Assert.AreEqual("1", ctx["Forwarded"]);
                }
            }

            async Task<IProtocolBridgingServicePrx> TestProxyAsync(IProtocolBridgingServicePrx prx, bool direct)
            {
                var ctx = new Dictionary<string, string>(prx.Context)
                {
                    { "MyCtx", "hello" }
                };

                _forwardedContext = null;
                Assert.AreEqual(await prx.OpAsync(13, ctx), 13);
                CheckContext(_forwardedContext!, direct);

                _forwardedContext = null;
                await prx.OpVoidAsync(ctx);
                CheckContext(_forwardedContext!, direct);

                _forwardedContext = null;
                (int v, string s) = await prx.OpReturnOutAsync(34, ctx);
                Assert.AreEqual(v, 34);
                Assert.AreEqual(s, "value=34");
                CheckContext(_forwardedContext!, direct);

                _forwardedContext = null;
                await prx.OpOnewayAsync(42);
                // Don't check the context, it might not yet be set, oneway returns as soon as the request was set.
                // CheckContext(ctxForwarded!, direct);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OpServiceNotFoundExceptionAsync());

                return prx.OpNewProxy().Clone(context: new Dictionary<string, string> { { "Direct", "1" } });
            }
        }

        private IProtocolBridgingServicePrx SetupForwarderServer(
            Protocol forwarderProtocol,
            Protocol targetProtocol,
            bool colocated)
        {
            _targetServer = new Server(_communicator, CreateServerOptions(targetProtocol, port: 0, colocated));
            var targetService = _targetServer.Add("target",
                                                  new ProtocolBridgingService(),
                                                  IProtocolBridgingServicePrx.Factory);
            _targetServer.Use(async (current, next, cancel) =>
            {
                _forwardedContext = current.Context;
                return await next();
            });
            _targetServer.Activate();

            _forwarderServer = new Server(_communicator, CreateServerOptions(forwarderProtocol, port: 1, colocated));
            var forwardService = _forwarderServer.Add("Forward",
                                                      new Forwarder(targetService),
                                                      IProtocolBridgingServicePrx.Factory);
            _forwarderServer.Activate();
            return forwardService;

            ServerOptions CreateServerOptions(Protocol protocol, int port, bool colocated) =>
                colocated ?
                    new ServerOptions()
                    {
                        ColocationScope = ColocationScope.Communicator,
                        Protocol = protocol
                    }
                    :
                    new ServerOptions()
                    {
                        ColocationScope = ColocationScope.None,
                        Endpoints = GetTestEndpoint(port: port, protocol: protocol)
                    };
        }

        internal class ProtocolBridgingService : IAsyncProtocolBridgingService
        {
            public ValueTask<int> OpAsync(int x, Current current, CancellationToken cancel) =>
                new (x);

            public ValueTask OpExceptionAsync(Current current, CancellationToken cancel) =>
                throw new ProtocolBridgingException(42);

            public ValueTask<IProtocolBridgingServicePrx> OpNewProxyAsync(Current current, CancellationToken cancel) =>
                new (IProtocolBridgingServicePrx.Factory.Create(current.Server,
                                                                current.Path).Clone(encoding: current.Encoding));

            public ValueTask OpOnewayAsync(int x, Current current, CancellationToken cancel) => default;

            public ValueTask<(int ReturnValue, string Y)> OpReturnOutAsync(
                int x,
                Current current,
                CancellationToken cancel) =>
                new ((x, $"value={x}"));

            public ValueTask OpServiceNotFoundExceptionAsync(Current current, CancellationToken cancel) =>
                throw new ServiceNotFoundException();

            public ValueTask OpVoidAsync(Current current, CancellationToken cancel) => default;
        }

        public sealed class Forwarder : IService
        {
            private readonly IServicePrx _target;

            ValueTask<OutgoingResponseFrame> IService.DispatchAsync(Current current, CancellationToken cancel)
            {
                current.Context["Forwarded"] = "1";
                return _target.ForwardAsync(current.IncomingRequestFrame, current.IsOneway, cancel: cancel);
            }

            internal Forwarder(IServicePrx target) => _target = target;
        }
    }
}
