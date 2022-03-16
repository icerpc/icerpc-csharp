// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class EndpointCacheTests
{
    [Test]
    public void Get_known_location_from_endpoint_cache()
    {
        var expected = Proxy.Parse("ice:/dummy");
        var location = new Location { IsAdapterId = true, Value = "hello" };
        IEndpointCache endpointCache = new EndpointCache(10);
        endpointCache.Set(location, expected);

        bool cached = endpointCache.TryGetValue(location, out (TimeSpan InsertionTime, Proxy Proxy) resolved);

        Assert.That(resolved.Proxy, Is.EqualTo(expected));
        Assert.That(cached, Is.True);
    }

    [Test]
    public void Get_unknown_location_from_endpoint_cache()
    {
        var location = new Location { IsAdapterId = true, Value = "hello" };
        IEndpointCache endpointCache = new EndpointCache(10);

        bool cached = endpointCache.TryGetValue(location, out (TimeSpan InsertionTime, Proxy Proxy) resolved);

        Assert.That(resolved.Proxy, Is.Null);
        Assert.That(cached, Is.False);
    }

    [Test]
    public void Remove_location_entry_from_endpoint_cache()
    {
        // Arrange
        IEndpointCache endpointCache = new EndpointCache(10);
        endpointCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, Proxy.Parse("ice:/dummy1"));

        // Act
        endpointCache.Remove(new Location { IsAdapterId = true, Value = "hello-1" });

        // Assert
        bool cached = endpointCache.TryGetValue(
            new Location { IsAdapterId = true, Value = "hello-1" },
            out (TimeSpan InsertionTime, Proxy Proxy) resolved);

        Assert.That(resolved.Proxy, Is.Null);
        Assert.That(cached, Is.False);
    }

    [Test]
    public void Endpoint_cache_prunes_oldest_entries_when_cache_reaches_max_cache_size()
    {
        // Arrange
        var expected = Proxy.Parse("ice:/dummy");
        IEndpointCache endpointCache = new EndpointCache(2);

        endpointCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, expected);
        endpointCache.Set(new Location { IsAdapterId = true, Value = "hello-2" }, expected);

        // Act
        endpointCache.Set(new Location { IsAdapterId = true, Value = "hello-3" }, expected);

        // Assert
        bool cached = endpointCache.TryGetValue(
            new Location { IsAdapterId = true, Value = "hello-1" },
            out (TimeSpan InsertionTime, Proxy Proxy) resolved);

        Assert.That(resolved.Proxy, Is.Null);
        Assert.That(cached, Is.False);
    }

    [Test]
    public void Updatate_existing_location_entry()
    {
        // Arrange
        var expected = Proxy.Parse("ice:/expected");
        IEndpointCache endpointCache = new EndpointCache(10);
        endpointCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, Proxy.Parse("ice:/dummy1"));

        // Act
        endpointCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, expected);

        // Assert
        bool cached = endpointCache.TryGetValue(
            new Location { IsAdapterId = true, Value = "hello-1" },
            out (TimeSpan InsertionTime, Proxy Proxy) resolved);

        Assert.That(resolved.Proxy, Is.EqualTo(expected));
        Assert.That(cached, Is.True);
    }
}
