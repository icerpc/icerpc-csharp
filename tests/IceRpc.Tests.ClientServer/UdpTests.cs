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
                // Depending on the system, the first send on the UDP socket will often succeed and the second will
                // fail because the error that results from the absence of a listening UDP server socket is received
                // asynchronously after the first send. We insert a small delay to ensure the error is received by
                // the UDP socket before the second send.
                await proxy.IcePingAsync(new Invocation { IsOneway = true });
                await Task.Delay(500);
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
            // The parsing of the parameters from the transport implementation fails because UDP parameters can
            // only be specified with an Ice1 endpoint string.

            await using var server = new Server
            {
                Endpoint = GetTestEndpoint(protocol: Protocol.Ice2, transport: "udp")
            };

            Assert.Throws<FormatException>(() => server.Listen());

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            Assert.ThrowsAsync<FormatException>(async () => await connection.ConnectAsync());
        }
    }
}
