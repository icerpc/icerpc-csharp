// Copyright (c) ZeroC, Inc.

using IceRpc.Locator.Internal;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

[Parallelizable(ParallelScope.All)]
public class ServerAddressCacheTests
{
    [Test]
    public void Get_known_location_from_server_address_cache()
    {
        var expected = new ServiceAddress(new Uri("ice:/dummy"));
        var location = new Location { IsAdapterId = true, Value = "hello" };
        IServerAddressCache serverAddressCache = new ServerAddressCache(10);
        serverAddressCache.Set(location, expected);

        bool cached = serverAddressCache.TryGetValue(location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) resolved);

        Assert.That(resolved.ServiceAddress, Is.EqualTo(expected));
        Assert.That(cached, Is.True);
    }

    [Test]
    public void Get_unknown_location_from_server_address_cache()
    {
        var location = new Location { IsAdapterId = true, Value = "hello" };
        IServerAddressCache serverAddressCache = new ServerAddressCache(10);

        bool cached = serverAddressCache.TryGetValue(location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) resolved);

        Assert.That(resolved.ServiceAddress, Is.Null);
        Assert.That(cached, Is.False);
    }

    [Test]
    public void Remove_location_entry_from_server_address_cache()
    {
        // Arrange
        IServerAddressCache serverAddressCache = new ServerAddressCache(10);
        serverAddressCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, new ServiceAddress(new Uri("ice:/dummy1")));

        // Act
        serverAddressCache.Remove(new Location { IsAdapterId = true, Value = "hello-1" });

        // Assert
        bool cached = serverAddressCache.TryGetValue(
            new Location { IsAdapterId = true, Value = "hello-1" },
            out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) resolved);

        Assert.That(resolved.ServiceAddress, Is.Null);
        Assert.That(cached, Is.False);
    }

    [Test]
    public void ServerAddress_cache_prunes_oldest_entries_when_cache_reaches_max_cache_size()
    {
        // Arrange
        var expected = new ServiceAddress(new Uri("ice:/dummy"));
        IServerAddressCache serverAddressCache = new ServerAddressCache(2);

        serverAddressCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, expected);
        serverAddressCache.Set(new Location { IsAdapterId = true, Value = "hello-2" }, expected);

        // Act
        serverAddressCache.Set(new Location { IsAdapterId = true, Value = "hello-3" }, expected);

        // Assert
        bool cached = serverAddressCache.TryGetValue(
            new Location { IsAdapterId = true, Value = "hello-1" },
            out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) resolved);

        Assert.That(resolved.ServiceAddress, Is.Null);
        Assert.That(cached, Is.False);
    }

    [Test]
    public void Update_existing_location_entry()
    {
        // Arrange
        var expected = new ServiceAddress(new Uri("ice:/expected"));
        IServerAddressCache serverAddressCache = new ServerAddressCache(10);
        serverAddressCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, new ServiceAddress(new Uri("ice:/dummy1")));

        // Act
        serverAddressCache.Set(new Location { IsAdapterId = true, Value = "hello-1" }, expected);

        // Assert
        bool cached = serverAddressCache.TryGetValue(
            new Location { IsAdapterId = true, Value = "hello-1" },
            out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) resolved);

        Assert.That(resolved.ServiceAddress, Is.EqualTo(expected));
        Assert.That(cached, Is.True);
    }
}
