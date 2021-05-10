// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using IceRpc.Interop;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class LocationResolverTests
    {
        private Communicator? _communicator;
        private Server? _server;

        [TestCase("ice+loc://testlocation/test", "ice+loc://unknown-location/test", "test", "test @ testlocation")]
        [TestCase("test @ adapter", "test @ unknown_adapter", "test", "ice+loc://adapter/test")]
        [TestCase("test", "test @ adapter", "test2", "ice+loc://adapter/test")]
        public async Task LocationResolver_ResolveAsync(string proxy, params string[] badProxies)
        {
            _communicator = new Communicator { IsInvoker = false };
            var pipeline = new Pipeline();

            var indirect = IGreeterTestServicePrx.Parse(proxy, pipeline);
            IGreeterTestServicePrx direct = SetupServer(indirect.Protocol, indirect.Path, pipeline);
            Assert.That(direct.Endpoint, Is.Not.Null);

            if (indirect.Endpoint is Endpoint locEndpoint)
            {
                pipeline.Use(LocationResolver(indirect.Endpoint.Host, category: null, direct.Endpoint!),
                             Interceptors.Binder(_communicator));
            }
            else
            {
                var identity = indirect.GetIdentity();
                pipeline.Use(LocationResolver(identity.Name, identity.Category, direct.Endpoint!),
                                  Interceptors.Binder(_communicator));
            }

            await indirect.SayHelloAsync();
            Assert.That(indirect.Connection, Is.Not.Null);

            foreach (string badProxy in badProxies)
            {
                var badGreeter = IGreeterTestServicePrx.Parse(badProxy, pipeline);
                Assert.ThrowsAsync<NoEndpointException>(async () => await badGreeter.SayHelloAsync());
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (_server != null)
            {
                await _server.ShutdownAsync();
            }
            if (_communicator != null)
            {
                await _communicator.ShutdownAsync();
            }
        }

        private IGreeterTestServicePrx SetupServer(Protocol protocol, string path, IInvoker invoker)
        {
            _server = new Server
            {
                Invoker = invoker,
                HasColocEndpoint = false,
                Dispatcher = new GreeterTestService(),
                Endpoint = protocol == Protocol.Ice2 ? "ice+tcp://127.0.0.1:0?tls=false" : "tcp -h 127.0.0.1 -p 0",
                ProxyHost = "localhost"
            };

            _server.Listen();

            // Need to create proxy after calling Listen; otherwise, the port number is still 0.
            var greeter = IGreeterTestServicePrx.FromServer(_server, path);
            Assert.AreNotEqual(0, greeter.Endpoint!.Port);
            return greeter;
        }

        // A very simple location resolver interceptor with no cache that resolves a single location represented by
        // location and category.
        private static Func<IInvoker, IInvoker> LocationResolver(
            string location,
            string? category,
            Endpoint resolvedEndpoint) =>
            next => new InlineInvoker(
                (request, cancel) =>
                {
                    if ((request.Protocol == resolvedEndpoint.Protocol) &&
                        ((request.Endpoint is Endpoint locEndpoint &&
                          locEndpoint.Transport == Transport.Loc &&
                          locEndpoint.Host == location &&
                           category == null) ||
                         (request.Endpoint == null &&
                          request.Protocol == Protocol.Ice1 &&
                          category != null &&
                          request.GetIdentity() == new Identity(location, category))))
                    {
                        request.Endpoint = resolvedEndpoint;
                        CollectionAssert.IsEmpty(request.AltEndpoints);
                    }
                    // else don't do anything

                    return next.InvokeAsync(request, cancel);
                });

        private class GreeterTestService : IGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
