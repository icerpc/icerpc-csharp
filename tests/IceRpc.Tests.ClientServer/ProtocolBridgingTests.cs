// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(scope: ParallelScope.All)]
    public class ProtocolBridgingTests : ClientServerBaseTest
    {
        [TestCase(Protocol.Ice2)]
        [TestCase(Protocol.Ice1)]
        public async Task ProtocolBridging_Forward(Protocol protocol)
        {
            Protocol other = protocol == Protocol.Ice1 ? Protocol.Ice2 : Protocol.Ice1;
            await using var serverForwarder = new Server(
                Communicator, 
                new ServerOptions() 
                {
                    Endpoints = GetTestEndpoint(port: 1, protocol: protocol) 
                });
            
            await using var serverSame = new Server(
                Communicator, 
                new ServerOptions()
                { 
                    Endpoints = GetTestEndpoint(port: 2, protocol: protocol)
                });
            
            await using var serverOther = new Server(
                Communicator,
                new ServerOptions()
                { 
                    Endpoints = GetTestEndpoint(port: 3, protocol: other) 
                });

            var samePrx = serverSame.Add("same", new ProtocolBridgingService(), IProtocolBridgingServicePrx.Factory);
            var otherPrx = serverOther.Add("other", new ProtocolBridgingService(), IProtocolBridgingServicePrx.Factory);

            serverForwarder.Add("ForwardSame", new Forwarder(samePrx));
            serverForwarder.Add("ForwardOther", new Forwarder(otherPrx));

            await serverForwarder.ActivateAsync();
            await serverSame.ActivateAsync();
            await serverOther.ActivateAsync();

            var forwardSamePrx = IProtocolBridgingServicePrx.Parse(
                GetTestProxy("ForwardSame", port: 1, protocol: protocol),
                Communicator);
            var forwardOtherPrx = IProtocolBridgingServicePrx.Parse(
                GetTestProxy("ForwardOther", port: 1, protocol: protocol),
                Communicator);

            // testing forwarding with same protocol
            var newPrx = await TestProxyAsync(forwardSamePrx);
            Assert.AreEqual(newPrx.Protocol, forwardSamePrx.Protocol);
            Assert.AreEqual(newPrx.Encoding, forwardSamePrx.Encoding);
            _ = await TestProxyAsync(newPrx);

            // testing forwarding with other protocol
            newPrx = await TestProxyAsync(forwardOtherPrx);
            Assert.AreNotEqual(newPrx.Protocol, forwardOtherPrx.Protocol);
            Assert.AreEqual(newPrx.Encoding, forwardOtherPrx.Encoding); // encoding must remain the same
            _ = await TestProxyAsync(newPrx);

            // testing forwarding with other protocol and other encoding
            Encoding encoding =
                forwardOtherPrx.Encoding == Encoding.V11 ? Encoding.V20 : Encoding.V11;
            newPrx = await TestProxyAsync(forwardOtherPrx.Clone(encoding: encoding));
            Assert.AreNotEqual(newPrx.Protocol, forwardOtherPrx.Protocol);
            Assert.AreEqual(newPrx.Encoding, encoding);
            _ = await TestProxyAsync(newPrx);

            static async Task<IProtocolBridgingServicePrx> TestProxyAsync(IProtocolBridgingServicePrx prx)
            {
                var ctx = new Dictionary<string, string>(prx.Context)
                {
                    { "MyCtx", "hello" }
                };

                Assert.AreEqual(await prx.OpAsync(13, ctx), 13);
                await prx.OpVoidAsync(ctx);

                (int v, string s) = await prx.OpReturnOutAsync(34);
                Assert.AreEqual(v, 34);
                Assert.AreEqual(s, "value=34");

                await prx.OpOnewayAsync(42);

                Assert.ThrowsAsync<ProtocolBridgingException>(async () => await prx.OpExceptionAsync());
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await prx.OpServiceNotFoundExceptionAsync());

                return prx.OpNewProxy().Clone(context: new Dictionary<string, string> { { "Direct", "1" } });
            }
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

            ValueTask<OutgoingResponseFrame> IService.DispatchAsync(Current current, CancellationToken cancel) =>
                _target.ForwardAsync(current.IncomingRequestFrame, current.IsOneway, cancel: cancel);

            internal Forwarder(IServicePrx target) => _target = target;
        }
    }
}
