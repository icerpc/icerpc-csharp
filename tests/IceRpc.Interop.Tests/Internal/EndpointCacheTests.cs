// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Internal.Tests;

[Parallelizable(ParallelScope.All)]
public class EndpointCacheTests
{
    [Test]
    public void EndpointCache_SetRemove()
    {
        var proxy = Proxy.Parse("ice:/dummy");

        var endpointCacheImpl = new EndpointCache(10);
        IEndpointCache endpointCache = endpointCacheImpl;

        for (int i = 0; i < 100; ++i)
        {
            endpointCache.Set(new Location { IsAdapterId = true, Value = $"{i}" }, proxy);
        }

        Assert.AreEqual(10, endpointCacheImpl.Count);

        // Make sure we kept the 10 most recent entries:
        for (int i = 90; i < 100; ++i)
        {
            Assert.That(
                endpointCache.TryGetValue(
                    new Location { IsAdapterId = true, Value = $"{i}" },
                    out var _),
                Is.True);
        }

        // Make sure removing an existing entry reduces the Count

        endpointCache.Remove(new Location{ IsAdapterId = true, Value = "20" });
        Assert.AreEqual(10, endpointCacheImpl.Count); // was not there
        endpointCache.Remove(new Location { IsAdapterId = true, Value = "95" });
        Assert.AreEqual(9, endpointCacheImpl.Count);
    }
}
