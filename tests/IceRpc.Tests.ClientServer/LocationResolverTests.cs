// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
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
        [TestCase("test", "test @ adapter", "test2", "ice+loc://adapter/test")]
        public async Task LocationResolver_ResolveAsync(string proxy, params string[] badProxies)
        {
            var greeter = IGreeterTestServicePrx.Parse(proxy, _communicator);
            Assert.AreEqual(greeter.Endpoints[0].Transport, Transport.Loc);

            ILocationResolver locationResolver = SetupServer(greeter.Protocol,
                                                             greeter.Path,
                                                             greeter.Endpoints[0].Host,
                                                             greeter.Endpoints[0]["category"]);

            Assert.IsNull(greeter.LocationResolver);
            Assert.ThrowsAsync<NoEndpointException>(async () => await greeter.SayHelloAsync());

            greeter = greeter.Clone(locationResolver: locationResolver);
            await greeter.SayHelloAsync();
            Assert.IsNotNull(greeter.GetCachedConnection());

            foreach (string badProxy in badProxies)
            {
                var badGreeter = IGreeterTestServicePrx.Parse(badProxy, _communicator);
                Assert.AreEqual(badGreeter.Endpoints[0].Transport, Transport.Loc);

                badGreeter = badGreeter.Clone(locationResolver: locationResolver);
                Assert.ThrowsAsync<NoEndpointException>(async () => await badGreeter.SayHelloAsync());
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _communicator.ShutdownAsync();
        }

        private ILocationResolver SetupServer(Protocol protocol, string path, string location, string? category)
        {
            Assert.IsTrue(protocol == Protocol.Ice1 || category == null);

            _server = new Server(_communicator,
                                 new ServerOptions()
                                 {
                                    ColocationScope = ColocationScope.None,
                                    // TODO: should GetTestEndpoint be capable of returning port 0?
                                    Endpoints =
                                        protocol == Protocol.Ice2 ? "ice+tcp://127.0.0.1:0" : "tcp -h 127.0.0.1 -p 0"
                                 });

            // direct proxy
            IGreeterTestServicePrx greeter =
                _server.Add(path, new GreeterTestService(), IGreeterTestServicePrx.Factory);

            _server.Activate();

            return new LocationResolver(protocol, location, category, greeter.Endpoints);
        }

        private class GreeterTestService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }

        // A very simple location resolver with no cache that resolves a single location represented by location and
        // category.
        private class LocationResolver : ILocationResolver
        {
            private string? _category;
            private string _location;

            private Protocol _protocol;
            private IReadOnlyList<Endpoint> _resolvedAddress;

            public ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveAsync(
                Endpoint endpoint,
                TimeSpan endpointsMaxAge,
                CancellationToken cancel)
            {
                Assert.AreEqual(endpoint.Transport, Transport.Loc);

                if (endpoint.Protocol == _protocol && endpoint.Host == _location && endpoint["category"] == _category)
                {
                    return new((_resolvedAddress, TimeSpan.Zero));
                }
                else
                {
                    return new((ImmutableList<Endpoint>.Empty, TimeSpan.Zero));
                }
            }

            internal LocationResolver(
                Protocol protocol,
                string location,
                string? category,
                IReadOnlyList<Endpoint> resolvedAddress)
            {
                _category = category;
                _location = location;
                _protocol = protocol;
                _resolvedAddress = resolvedAddress;
            }
        }
    }
}
