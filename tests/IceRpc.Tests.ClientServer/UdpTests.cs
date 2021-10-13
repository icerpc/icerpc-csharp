// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(5000)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    public class UdpTests : ClientServerBaseTest
    {

        [Test]
        public async Task Udp_Invoke()
        {
            var source = new TaskCompletionSource<string>();
            await using var server = new Server
            {
                Dispatcher = new InlineDispatcher((request, cancel) =>
                    {
                        source.TrySetResult(request.Operation);
                        return new(OutgoingResponse.ForPayload(request, default));
                    }),
                Endpoint = GetTestEndpoint(protocol: Protocol.Ice1, transport: "udp")
            };
            server.Listen();

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            await connection.ConnectAsync();

            var proxy = ServicePrx.FromConnection(connection);
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            Assert.That(await source.Task.WaitAsync(TimeSpan.FromSeconds(1)), Is.EqualTo("ice_ping"));
        }

        [Test]
        public async Task Udp_ConnectFailure()
        {
            await using var connection = new Connection
            {
                RemoteEndpoint = GetTestEndpoint(protocol: Protocol.Ice1, transport: "udp"),
            };
            await connection.ConnectAsync();

            var proxy = ServicePrx.FromConnection(connection);
            Assert.CatchAsync<TransportException>(async () =>
            {
                // It can take few invocations before failing
                await proxy.IcePingAsync(new Invocation { IsOneway = true });
                await proxy.IcePingAsync(new Invocation { IsOneway = true });
                await proxy.IcePingAsync(new Invocation { IsOneway = true });
            });
        }

        [Test]
        public async Task Udp_ConnectSuccess()
        {
            await using var server = new Server
            {
                Endpoint = GetTestEndpoint(protocol: Protocol.Ice1, transport: "udp")
            };
            server.Listen();

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            await connection.ConnectAsync();
        }

        [Test]
        public async Task Udp_Ice2NotSupported()
        {
            await using var server = new Server
            {
                Endpoint = GetTestEndpoint(protocol: Protocol.Ice2, transport: "udp")
            };
            Assert.Throws<Transports.UnknownTransportException>(() => server.Listen());

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            Assert.ThrowsAsync<Transports.UnknownTransportException>(async () => await connection.ConnectAsync());
        }
    }
}
