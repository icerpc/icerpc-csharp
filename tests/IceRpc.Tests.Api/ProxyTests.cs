// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class ProxyTests
    {
        [Test]
        /// <summary>Check that the proxy hash collision rate fall within acceptable limits.</summary>
        public async Task Proxy_Hash_CollisionRate_Test()
        {
            int maxCollisions = 10;
            int maxIterations = 10000;

            int collisions = await CheckHashCollisionsAsync(maxIterations, maxCollisions, obj => obj.GetHashCode());
            Assert.LessOrEqual(collisions, maxCollisions);

            collisions = await CheckHashCollisionsAsync(maxIterations,
                                                        maxCollisions,
                                                        obj => ProxyComparer.Identity.GetHashCode(obj));
            Assert.LessOrEqual(collisions, maxCollisions);

            collisions = await CheckHashCollisionsAsync(maxIterations,
                                                        maxCollisions,
                                                        obj => ProxyComparer.IdentityAndFacet.GetHashCode(obj));
            Assert.LessOrEqual(collisions, maxCollisions);

            static async Task<int> CheckHashCollisionsAsync(
                int maxIterations,
                int maxCollisions,
                Func<IServicePrx, int> hasher)
            {
                var rand = new Random();
                await using var communicator = new Communicator();
                int proxyCollisions = 0;
                var seenProxy = new Dictionary<int, IServicePrx>();
                for (int i = 0; proxyCollisions < maxCollisions && i < maxIterations; ++i)
                {
                    var obj = IServicePrx.Parse($"ice+tcp://host-{rand.Next(100)}:{rand.Next(65536)}/{i}", communicator);

                    // Check the same proxy produce always the same hash
                    int hash = hasher(obj);
                    Assert.AreEqual(hash, hasher(obj));

                    if (seenProxy.ContainsKey(hash))
                    {
                        if (obj.Equals(seenProxy[hash]))
                        {
                            continue;
                        }
                        ++proxyCollisions;
                    }
                    else
                    {
                        seenProxy[hash] = obj;
                    }
                }
                return proxyCollisions;
            }
        }
    }
}
