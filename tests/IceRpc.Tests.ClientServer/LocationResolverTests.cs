// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class LocationResolverTests
    {
        private readonly Communicator _communicator;
        private Server _server = null!;

        public LocationResolverTests() => _communicator = new Communicator();

        [TestCase("ice+loc://testlocation/test", "ice+loc://unknown-location/test", "test", "test @ testlocation")]
        [TestCase("test @ adapter", "test @ unknown_adapter", "test", "ice+loc://adapter/test")]
        public async Task LocationResolver_ResolveAsync(string proxy, params string[] badProxies)
        {
            var greeter = IGreeterTestServicePrx.Parse(proxy, _communicator);

            Assert.AreEqual(Transport.Loc, greeter.Endpoint!.Transport);

            ILocationResolver locationResolver = SetupServer(greeter.Protocol,
                                                             greeter.Path,
                                                             greeter.Endpoint.Host);

            Assert.ThrowsAsync<NoEndpointException>(async () => await greeter.SayHelloAsync());

            _communicator.LocationResolver = locationResolver;
            await greeter.SayHelloAsync();
            Assert.IsNotNull(greeter.Connection);

            foreach (string badProxy in badProxies)
            {
                var badGreeter = IGreeterTestServicePrx.Parse(badProxy, _communicator);
                Assert.ThrowsAsync<NoEndpointException>(async () => await badGreeter.SayHelloAsync());
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _communicator.ShutdownAsync();
        }

        private ILocationResolver SetupServer(Protocol protocol, string path, string location)
        {
            _server = new Server
            {
                Invoker = _communicator,
                HasColocEndpoint = false,
                Dispatcher = new GreeterTestService(),
                // TODO: should GetTestEndpoint be capable of returning port 0?
                Endpoint = protocol == Protocol.Ice2 ? "ice+tcp://127.0.0.1:0?tls=false" : "tcp -h 127.0.0.1 -p 0",
                ProxyHost = "localhost"
            };

            _server.Listen();

            // Need to create proxy after calling Listen; otherwise, the port number is still 0.
            var greeter = IGreeterTestServicePrx.FromServer(_server, path);

            Assert.AreNotEqual(0, greeter.Endpoint!.Port);

            return new LocationResolver(protocol, location, greeter.Endpoint);
        }

        private class GreeterTestService : IGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        // A very simple location resolver with no cache that resolves a single location
        private class LocationResolver : ILocationResolver
        {
            private readonly string _location;

            private readonly Protocol _protocol;
            private readonly Endpoint _resolvedEndpoint;

            public ValueTask<(Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints)> ResolveAsync(
                Endpoint locEndpoint,
                bool refreshCache,
                CancellationToken cancel)
            {
                Assert.AreEqual(Transport.Loc, locEndpoint.Transport);

                Endpoint? endpoint =
                    locEndpoint.Protocol == _protocol && locEndpoint.Host == _location ? _resolvedEndpoint : null;

                return new((endpoint, ImmutableList<Endpoint>.Empty));
            }

            internal LocationResolver(
                Protocol protocol,
                string location,
                Endpoint resolvedEndpoint)
            {
                _location = location;
                _protocol = protocol;
                _resolvedEndpoint = resolvedEndpoint;
            }
        }
    }
}
