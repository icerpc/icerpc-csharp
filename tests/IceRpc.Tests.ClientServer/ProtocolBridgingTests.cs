// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(10000)]
    public class ProtocolBridgingTests : ClientServerBaseTest
    {
        private Communicator Communicator { get; }
        private List<Server> Servers { get; } = new List<Server>();
        private SortedDictionary<string, string>? ForwardedContext {get; set; }

        public ProtocolBridgingTests() => Communicator = new Communicator();

        [TearDown]
        public async Task TearDownAsync()
        {
            foreach (var server in Servers)
            {
                try
                {
                    await server.DisposeAsync();
                }
                catch
                {
                }
            }
            await Communicator.DisposeAsync();
        }

        [TestCase(Protocol.Ice2, true)]
        [TestCase(Protocol.Ice2, false)]
        // TODO enable once we fix https://github.com/zeroc-ice/icerpc-csharp/issues/140
        // [TestCase(Protocol.Ice1, true)]
        [TestCase(Protocol.Ice1, false)]
        public async Task ProtocolBridging_Forward(Protocol protocol, bool colocated)
        {
            Protocol other = protocol == Protocol.Ice1 ? Protocol.Ice2 : Protocol.Ice1;

            var samePrx = await SetupServer("same", protocol, 1, colocated);
            var otherPrx = await SetupServer("other", other, 2, colocated);

            (IProtocolBridgingServicePrx forwardSamePrx, IProtocolBridgingServicePrx forwardOtherPrx) =
                await SetupForwarderServer(samePrx, otherPrx, protocol, colocated);

            // testing forwarding with same protocol
            var newPrx = await TestProxyAsync(forwardSamePrx, false);
            Assert.AreEqual(newPrx.Protocol, forwardSamePrx.Protocol);
            Assert.AreEqual(newPrx.Encoding, forwardSamePrx.Encoding);
            _ = await TestProxyAsync(newPrx, true);

            // testing forwarding with other protocol
            newPrx = await TestProxyAsync(forwardOtherPrx, false);
            Assert.AreNotEqual(newPrx.Protocol, forwardOtherPrx.Protocol);
            Assert.AreEqual(newPrx.Encoding, forwardOtherPrx.Encoding); // encoding must remain the same
            _ = await TestProxyAsync(newPrx, true);

            // testing forwarding with other protocol and other encoding
            Encoding encoding =
                forwardOtherPrx.Encoding == Encoding.V11 ? Encoding.V20 : Encoding.V11;
            newPrx = await TestProxyAsync(forwardOtherPrx.Clone(encoding: encoding), false);
            Assert.AreNotEqual(newPrx.Protocol, forwardOtherPrx.Protocol);
            Assert.AreEqual(newPrx.Encoding, encoding);
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

                ForwardedContext = null;
                Assert.AreEqual(await prx.OpAsync(13, ctx), 13);
                CheckContext(ForwardedContext!, direct);

                ForwardedContext = null;
                await prx.OpVoidAsync(ctx);
                CheckContext(ForwardedContext!, direct);

                ForwardedContext = null;
                (int v, string s) = await prx.OpReturnOutAsync(34, ctx);
                Assert.AreEqual(v, 34);
                Assert.AreEqual(s, "value=34");
                CheckContext(ForwardedContext!, direct);

                ForwardedContext = null;
                await prx.OpOnewayAsync(42);
                // Don't check the context, it might not yet be set, oneway returns as soon as the request was set.
                // CheckContext(ctxForwarded!, direct);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OpServiceNotFoundExceptionAsync());

                return prx.OpNewProxy().Clone(context: new Dictionary<string, string> { { "Direct", "1" } });
            }
        }

        private async Task<IProtocolBridgingServicePrx> SetupServer(
            string path,
            Protocol protocol,
            int port = 0,
            bool colocated = false)
        {
            var serverOptions = colocated ?
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

            var server = new Server(Communicator, serverOptions);
            var prx = server.Add(path, new ProtocolBridgingService(), IProtocolBridgingServicePrx.Factory);
            server.Use(async (current, next, cancel) =>
            {
                ForwardedContext = current.Context;
                return await next();
            });
            await server.ActivateAsync();
            Servers.Add(server);
            return prx;
        }

        private async Task<(IProtocolBridgingServicePrx, IProtocolBridgingServicePrx)> SetupForwarderServer(
            IProtocolBridgingServicePrx samePrx,
            IProtocolBridgingServicePrx otherPrx,
            Protocol protocol,
            bool colocated)
        {
            var serverOptions = colocated ?
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.Communicator,
                    Protocol = protocol
                }
                :
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 0, protocol: protocol)
                };

            var server = new Server(Communicator, serverOptions);
            var forwardSamePrx = server.Add("ForwardSame",
                                            new Forwarder(samePrx),
                                            IProtocolBridgingServicePrx.Factory);
            var forwardOtherPrx = server.Add("ForwardOther",
                                             new Forwarder(otherPrx),
                                             IProtocolBridgingServicePrx.Factory);
            await server.ActivateAsync();
            Servers.Add(server);
            return (forwardSamePrx, forwardOtherPrx);
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
