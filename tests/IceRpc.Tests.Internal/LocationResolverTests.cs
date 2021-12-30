// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(ParallelScope.All)]
    public class LocationResolverTests
    {
        [Test]
        public async Task CacheLessLocationResolver_ResolveAsync()
        {
            var endpointFinder = new FakeEndpointFinder();

            ILocationResolver locationResolver = new CacheLessLocationResolver(endpointFinder);

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
                    location.AdapterId == "good" ?
                        Proxy.Parse("dummy:tcp -h localhost -p 10000", format: IceProxyFormat.Default) : null);
        }
    }
}
