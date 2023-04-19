// Copyright (c) ZeroC, Inc.

using IceRpc.Locator.Internal;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Locator.Tests;

[Parallelizable(ParallelScope.All)]
public class CacheLessLocationResolverTests
{
    [Test]
    public async Task Resolving_a_known_location_returns_a_proxy(
        [Values(true, false)] bool isAdapterId,
        [Values(true, false)] bool refreshCache)
    {
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost:10000/dummy"));
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeServerAddressFinder(expectedServiceAddress));

        (ServiceAddress? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = isAdapterId ? "indirect" : "good" },
                refreshCache: refreshCache,
                cancellationToken: default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress));
        Assert.That(fromCache, Is.False);
    }

    [Test]
    public async Task Resolving_an_unknown_location_returns_null(
        [Values(true, false)] bool isAdapterId,
        [Values(true, false)] bool refreshCache)
    {
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost:10000/dummy"));
        ILocationResolver locationResolver = new CacheLessLocationResolver(new FakeServerAddressFinder(expectedServiceAddress));

        (ServiceAddress? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = "bad" },
                refreshCache: refreshCache,
                cancellationToken: default);

        Assert.That(serviceAddress, Is.Null);
        Assert.That(fromCache, Is.False);
    }

    private sealed class FakeServerAddressFinder : IServerAddressFinder
    {
        private readonly ServiceAddress _serviceAddress;

        public FakeServerAddressFinder(ServiceAddress serviceAddress) => _serviceAddress = serviceAddress;

        Task<ServiceAddress?> IServerAddressFinder.FindAsync(Location location, CancellationToken cancellationToken) =>
            (location.Value, location.IsAdapterId) switch
            {
                ("indirect", true) => Task.FromResult<ServiceAddress?>(
                        new ServiceAddress(Protocol.Ice)
                        {
                            Params = new Dictionary<string, string> { ["adapter-id"] = "good" }.ToImmutableDictionary()
                        }),
                ("good", _) => Task.FromResult<ServiceAddress?>(_serviceAddress),
                _ => Task.FromResult<ServiceAddress?>(null)

            };
    }
}
