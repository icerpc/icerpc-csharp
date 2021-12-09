// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

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
                        return new(new OutgoingResponse(request));
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
                RemoteEndpoint = "udp -h 127.0.0.1 -p 4061"
            };
            await connection.ConnectAsync();

            var proxy = ServicePrx.FromConnection(connection);
            // We're sending a UDP request to an unreachable port. We get back a "destination port unreachable"
            // ICMP packet and close the connection, which results in the second ping failing with a Connection
            // closed exception.
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            await Task.Delay(500);

            Assert.CatchAsync<ConnectionClosedException>(async () =>
                await proxy.IcePingAsync(new Invocation { IsOneway = true }));
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

            // udp is not registered as a multiplexed transport
            Assert.Throws<UnknownTransportException>(() => server.Listen());

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            Assert.ThrowsAsync<UnknownTransportException>(async () => await connection.ConnectAsync());
        }
    }
}
