// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    public class UdpTests
    {
        [Test]
        public async Task Udp_Invoke()
        {
            var source = new TaskCompletionSource<string>();
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol("ice")
                .UseTransport("udp")
                .AddTransient<IDispatcher>(_ => new InlineDispatcher((request, cancel) =>
                    {
                        source.TrySetResult(request.Operation);
                        return new(new OutgoingResponse(request));
                    }))
                .BuildServiceProvider();

            ServicePrx proxy = serviceProvider.GetProxy<ServicePrx>();
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            Assert.That(await source.Task.WaitAsync(TimeSpan.FromSeconds(1)), Is.EqualTo("ice_ping"));
        }

        [Test]
        public async Task Udp_OnewayOnly()
        {
            var source = new TaskCompletionSource<string>();
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol("ice")
                .UseTransport("udp")
                .BuildServiceProvider();

            ServicePrx proxy = serviceProvider.GetProxy<ServicePrx>();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await proxy.IcePingAsync());
            await proxy.IcePingAsync(new Invocation { IsOneway = true });

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await proxy.IceIdsAsync(new Invocation { IsOneway = true }));
        }

        [Test]
        public async Task Udp_ConnectFailure()
        {
            await using var connection = new Connection("ice://127.0.0.1:4061?transport=udp");
            await connection.ConnectAsync();

            var proxy = ServicePrx.FromConnection(connection);
            // We're sending a UDP request to an unreachable port. We get back a "destination port unreachable"
            // ICMP packet and close the connection, which results in the second ping failing with a Connection
            // closed exception.
            await proxy.IcePingAsync(new Invocation { IsOneway = true });
            await Task.Delay(500);
        }

        [Test]
        public async Task Udp_ConnectSuccess()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol("ice")
                .UseTransport("udp")
                .BuildServiceProvider();
            await serviceProvider.GetRequiredService<Connection>().ConnectAsync();
        }

        [Test]
        public async Task Udp_IceRpcNotSupported()
        {
            await using var server = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://[::0]:0?transport=udp");

            // udp is not registered as a multiplexed transport
            Assert.Throws<UnknownTransportException>(() => server.Listen());

            await using var connection = new Connection(server.Endpoint);
            Assert.ThrowsAsync<UnknownTransportException>(async () => await connection.ConnectAsync());
        }
    }
}
