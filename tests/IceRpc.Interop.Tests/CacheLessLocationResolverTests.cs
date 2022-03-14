// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class CacheLessLocationResolverTests
{
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Resolve_async_known_entry(bool isAdapterId, bool refreshCache)
    {
        var expectedProxy = Proxy.Parse("dummy:tcp -h localhost -p 10000");
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeEndpointFinder(expectedProxy));

        (Proxy? proxy, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = "good" },
                refreshCache: refreshCache,
                cancel: default);

        Assert.That(proxy, Is.EqualTo(expectedProxy));
        Assert.That(fromCache, Is.False);
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Resolve_async_unknown_entry(bool isAdapterId, bool refreshCache)
    {
        var expectedProxy = Proxy.Parse("dummy:tcp -h localhost -p 10000");
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
