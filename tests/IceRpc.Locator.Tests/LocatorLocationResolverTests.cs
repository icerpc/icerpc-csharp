// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

public class LocatorLocationResolverTests
{
    [TestCase(0, 2)]
    [TestCase(1, 1)]
    public async Task Location_resolver_cache(int maxCacheSize, int resolveCalls)
    {
        // Arrange
        var locator = new FakeLocator(new ServiceAddress(new Uri("ice://localhost/dummy:10000")), false);
        var locatorLocationResolver = new LocatorLocationResolver(
            locator,
            new LocatorOptions
            {
                Ttl = Timeout.InfiniteTimeSpan,
                RefreshThreshold = TimeSpan.FromSeconds(1),
                MaxCacheSize = maxCacheSize
            },
            NullLogger.Instance);

        (var resolvedAddress1, _) = await locatorLocationResolver.ResolveAsync(
            new Location { Value = "good", IsAdapterId = false },
            refreshCache: false,
            CancellationToken.None);

        (var resolvedAddress2, _) = await locatorLocationResolver.ResolveAsync(
            new Location { Value = "good", IsAdapterId = false },
            refreshCache: false,
            CancellationToken.None);

        Assert.That(locator.Resolved, Is.EqualTo(resolveCalls));
        Assert.That(resolvedAddress1, Is.Not.Null);
        Assert.That(resolvedAddress1, Is.EqualTo(resolvedAddress2));
    }

    [Test]
    public void Ttl_cannot_be_smaller_than_the_refresh_timeout() =>
        Assert.That(
            () => new LocatorLocationResolver(
                new FakeLocator(new ServiceAddress(Protocol.Ice), false),
                new LocatorOptions
                {
                    Ttl = TimeSpan.FromSeconds(1),
                    RefreshThreshold = TimeSpan.FromSeconds(10)
                },
                NullLogger.Instance),
            Throws.InvalidOperationException);
}
