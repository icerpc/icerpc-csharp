// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    public class UdpTests
    {
        private const string Message = "hello, world";

        [Test]
        public async Task Udp_Invoke()
        {
            var source = new TaskCompletionSource<string>();
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
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
        }

        [Test]
        public async Task Udp_ConnectSuccess()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseTransport("udp")
                .BuildServiceProvider();
            await serviceProvider.GetRequiredService<Connection>().ConnectAsync();
        }

        [Test]
        public async Task Udp_Ice2NotSupported()
        {
            await using var server = new Server
            {
                Endpoint = "ice+udp://[::0]:0"
            };

            // udp is not registered as a multiplexed transport
            Assert.Throws<UnknownTransportException>(() => server.Listen());

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            Assert.ThrowsAsync<UnknownTransportException>(async () => await connection.ConnectAsync());
        }

        [Test]
        public async Task Udp_SplitInTwo()
        {
            var source = new TaskCompletionSource<string>();

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseTransport("udp")
                .AddTransient<IDispatcher, Greeter>()
                .AddTransient<IInvoker>(_ =>
                {
                    var pipeline = new Pipeline();
                    pipeline.Use(next => new InlineInvoker((request, cancel) =>
                    {
                        source.TrySetResult(request.Operation);
                        request.PayloadSink = new SplitInTwoPipeWriterDecorator(request.PayloadSink);
                        return next.InvokeAsync(request, cancel);
                    }));
                    return pipeline;
                })
                .BuildServiceProvider();

            _ = new Greeter(); // TODO: otherwise, the compiler does not see the ussable from AddTransient above

            GreeterPrx proxy = serviceProvider.GetProxy<GreeterPrx>();
            await proxy.SayHelloAsync(Message, new Invocation { IsOneway = true });
            Assert.That(await source.Task.WaitAsync(TimeSpan.FromSeconds(1)), Is.EqualTo("sayHello"));
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                Assert.That(message, Is.EqualTo(Message));
                return default;
            }
        }

        private class SplitInTwoPipeWriterDecorator : PipeWriterDecorator
        {
            public SplitInTwoPipeWriterDecorator(PipeWriter decoratee)
                : base(decoratee)
            {
            }

            // We split the WriteAsync in 2 WriteAsync when there are at least 2 bytes
            public override async ValueTask<FlushResult> WriteAsync(
                ReadOnlyMemory<byte> source,
                CancellationToken cancellationToken)
            {
                if (source.Length >= 2)
                {
                    int split = source.Length / 2;
                    _ = await Decoratee.WriteAsync(source[0..split], cancellationToken);
                    return await Decoratee.WriteAsync(source[split..], cancellationToken);
                }
                else
                {
                    return await Decoratee.WriteAsync(source, cancellationToken);
                }
            }
        }
    }
}
