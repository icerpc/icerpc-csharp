// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
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
                endpointCache.Set(new Location($"{i}"), proxy);
            }

            Assert.AreEqual(10, endpointCacheImpl.Count);

            // Make sure we kept the 10 most recent entries:
            for (int i = 90; i < 100; ++i)
            {
                Assert.That(endpointCache.TryGetValue(new Location($"{i}"), out var _), Is.True);
            }

            // Make sure removing an existing entry reduces the Count

            endpointCache.Remove(new Location("20"));
            Assert.AreEqual(10, endpointCacheImpl.Count); // was not there
            endpointCache.Remove(new Location("95"));
            Assert.AreEqual(9, endpointCacheImpl.Count);
        }
    }
}
