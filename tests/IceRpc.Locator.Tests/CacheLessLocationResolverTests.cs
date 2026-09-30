// Copyright (c) ZeroC, Inc.

using IceRpc.Locator.Internal;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

[Parallelizable(ParallelScope.All)]
public class CacheLessLocationResolverTests
{
    [Test]
    public async Task Resolving_a_known_location_returns_a_proxy([Values] bool isAdapterId, [Values] bool refreshCache)
    {
        var expectedServiceAddress = new ServiceAddress.Ice(new Uri("ice://localhost:10000/greeter"));
        ILocationResolver locationResolver =
            new CacheLessLocationResolver(new FakeServerAddressFinder(expectedServiceAddress));

        (ServiceAddress.Ice? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = isAdapterId ? "good" : "/good" },
                refreshCache: refreshCache,
                cancellationToken: default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress));
        Assert.That(fromCache, Is.False);
    }

    [Test]
    public async Task Resolving_a_known_location_returns_a_proxy_indirectly([Values] bool refreshCache)
    {
        var expectedServiceAddress = new ServiceAddress.Ice(new Uri("ice://localhost:10000/greeter"));

        // The identity of the intermediary indirect proxy is ignored.
        var intermediary = new ServiceAddress.Ice(new Uri("ice:/dummy?adapter-id=GoodAdapter"));

        ILocationResolver locationResolver =
            new CacheLessLocationResolver(new FakeServerAddressFinder(expectedServiceAddress, intermediary));

        (ServiceAddress.Ice? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = false, Value = "/good" },
                refreshCache: refreshCache,
                cancellationToken: default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress));
        Assert.That(fromCache, Is.False);
    }

    [Test]
    public async Task Resolving_an_unknown_location_returns_null([Values] bool isAdapterId, [Values] bool refreshCache)
    {
        var expectedServiceAddress = new ServiceAddress.Ice(new Uri("ice://localhost:10000/greeter"));
        ILocationResolver locationResolver =
            new CacheLessLocationResolver(new FakeServerAddressFinder(expectedServiceAddress));

        (ServiceAddress.Ice? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = isAdapterId, Value = isAdapterId ? "bad" : "/bad" },
                refreshCache: refreshCache,
                cancellationToken: default);

        Assert.That(serviceAddress, Is.Null);
        Assert.That(fromCache, Is.False);
    }

    [Test]
    public async Task Resolving_an_unknown_location_returns_null_indirectly([Values] bool refreshCache)
    {
        var expectedServiceAddress = new ServiceAddress.Ice(new Uri("ice://localhost:10000/greeter"));
        var intermediary = new ServiceAddress.Ice(new Uri("ice:/xxx?adapter-id=BadAdapter"));
        ILocationResolver locationResolver =
            new CacheLessLocationResolver(new FakeServerAddressFinder(expectedServiceAddress, intermediary));

        (ServiceAddress.Ice? serviceAddress, bool fromCache) =
            await locationResolver.ResolveAsync(
                new Location { IsAdapterId = false, Value = "/any" },
                refreshCache: refreshCache,
                cancellationToken: default);

        Assert.That(serviceAddress, Is.Null);
        Assert.That(fromCache, Is.False);
    }

    private sealed class FakeServerAddressFinder : IServerAddressFinder
    {
        private readonly ServiceAddress.Ice? _intermediary;
        private readonly ServiceAddress.Ice _target;

        public Task<ServiceAddress.Ice?> FindAsync(Location location, CancellationToken cancellationToken)
        {
            if (_intermediary is null)
            {
                return Task.FromResult(location.Value == "good" || location.Value == "/good" ? _target : null);
            }
            else if (location.IsAdapterId)
            {
                return Task.FromResult(location.Value == "GoodAdapter" ? _target : null);
            }
            else
            {
                return Task.FromResult<ServiceAddress.Ice?>(_intermediary);
            }
        }

        internal FakeServerAddressFinder(ServiceAddress.Ice target, ServiceAddress.Ice? intermediary = null)
        {
            _target = target;
            _intermediary = intermediary;
        }
    }
}
