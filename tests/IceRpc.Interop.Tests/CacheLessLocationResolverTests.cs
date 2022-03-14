// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class CacheLessLocationResolverTests
{
    [Test]
    public async Task Resolve_known_location_returns_a_proxy(
        [Values(true, false)] bool isAdapterId,
        [Values(true, false)] bool refreshCache)
    {
        var expectedProxy = Proxy.Parse("dummy:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeEndpointFinder(expectedProxy));

        (Proxy? proxy, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = "good" },
                refreshCache: refreshCache,
                cancel: default);

        Assert.That(proxy, Is.EqualTo(expectedProxy));
        Assert.That(fromCache, Is.False);
    }

    [Test]
    public async Task Resolve_unknown_location_returns_null(
        [Values(true, false)] bool isAdapterId,
        [Values(true, false)] bool refreshCache)
    {
        var expectedProxy = Proxy.Parse("dummy:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeEndpointFinder(expectedProxy));

        (Proxy? proxy, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = "bad" },
                refreshCache: refreshCache,
                cancel: default);

        Assert.That(proxy, Is.Null);
        Assert.That(fromCache, Is.False);
    }

    private class FakeEndpointFinder : IEndpointFinder
    {
        private readonly Proxy _proxy;

        public FakeEndpointFinder(Proxy proxy) => _proxy = proxy;

        Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel) =>
            Task.FromResult(location.Value == "good" ? _proxy : null);
    }
}
