// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(ParallelScope.All)]
    public class LocationResolverTests
    {
        [Test]
        public async Task LocationResolver_NoCacheAsync()
        {
            // TODO: background etc. are meaningful only when cache is not null.

            var endpointFinder = new FakeEndpointFinder();

            ILocationResolver locationResolver =
                new LocationResolver(endpointFinder,
                                     endpointCache: null,
                                     background: false,
                                     justRefreshedAge: TimeSpan.Zero,
                                     ttl: TimeSpan.Zero);

            (Proxy? proxy, bool fromCache) =
                await locationResolver.ResolveAsync(new Location("good"), refreshCache: false, cancel: default);

            Assert.That(proxy, Is.Not.Null);
            Assert.That(fromCache, Is.False);

            (proxy, fromCache) =
                await locationResolver.ResolveAsync(new Location("good"), refreshCache: true, cancel: default);

            Assert.That(proxy, Is.Not.Null);
            Assert.That(fromCache, Is.False);

            (proxy, fromCache) =
                await locationResolver.ResolveAsync(new Location("bad"), refreshCache: false, cancel: default);

            Assert.That(proxy, Is.Null);
            Assert.That(fromCache, Is.False);

            (proxy, fromCache) =
                await locationResolver.ResolveAsync(new Location("bad"), refreshCache: true, cancel: default);

            Assert.That(proxy, Is.Null);
            Assert.That(fromCache, Is.False);
        }

        private class FakeEndpointFinder : IEndpointFinder
        {
            Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel) =>
                Task.FromResult<Proxy?>(
                    location.AdapterId == "good" ? Proxy.Parse("dummy:tcp -h localhost -p 10000") : null);
        }
    }
}
