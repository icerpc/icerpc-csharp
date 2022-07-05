// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Locator.Internal;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

[Parallelizable(ParallelScope.All)]
public class CacheLessLocationResolverTests
{
    [Test]
    public async Task Resolving_a_known_location_returns_a_proxy(
        [Values(true, false)] bool isAdapterId,
        [Values(true, false)] bool refreshCache)
    {
        var expectedServiceAddress = ServiceAddress.Parse("ice://localhost:10000/dummy");
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeEndpointFinder(expectedServiceAddress));

        (ServiceAddress? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = "good" },
                refreshCache: refreshCache,
                cancel: default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress));
        Assert.That(fromCache, Is.False);
    }

    [Test]
    public async Task Resolving_an_unknown_location_returns_null(
        [Values(true, false)] bool isAdapterId,
        [Values(true, false)] bool refreshCache)
    {
        var expectedServiceAddress = ServiceAddress.Parse("ice://localhost:10000/dummy");
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeEndpointFinder(expectedServiceAddress));

        (ServiceAddress? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = "bad" },
                refreshCache: refreshCache,
                cancel: default);

        Assert.That(serviceAddress, Is.Null);
        Assert.That(fromCache, Is.False);
    }

    private class FakeEndpointFinder : IEndpointFinder
    {
        private readonly ServiceAddress _serviceAddress;

        public FakeEndpointFinder(ServiceAddress serviceAddress) => _serviceAddress = serviceAddress;

        Task<ServiceAddress?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel) =>
            Task.FromResult(location.Value == "good" ? _serviceAddress : null);
    }
}
