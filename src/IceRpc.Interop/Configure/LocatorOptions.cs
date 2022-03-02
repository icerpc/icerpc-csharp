// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Configure
{
    /// <summary>An options class for configuring a Locator interceptor.</summary>
    public sealed record class LocatorOptions
    {
        /// <summary>Gets or initializes the background configuration.</summary>
        /// <value>When <c>true</c>, if the lookup finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is <c>false</c>, meaning the
        /// lookupdoes not return stale values.</value>
        public bool Background { get; init; }

        /// <summary>Gets or initializes the maximum size of the cache.</summary>
        /// <value>The maximum size of the cache. 0 means no cache. The default is 100.</value>
        public int CacheMaxSize
        {
            get => _cacheMaxSize;
            init => _cacheMaxSize = value >= 0 ? value :
                throw new ArgumentException($"{nameof(CacheMaxSize)} must be positive", nameof(value));
        }

        /// <summary>Gets or initializes the just refreshed age. When a cache entry's age is <c>JustRefreshedAge</c> or
        /// less, it's considered just refreshed and won't be updated even when the caller requests a refresh.</summary>
        /// <value>The just refreshed age. The default is 1s.</value>
        public TimeSpan JustRefreshedAge { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>Gets or initializes the logger factory used to create the IceRpc logger.</summary>
        public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

        /// <summary>Gets or initializes the time-to-live. After ttl, a cache entry is considered stale.</summary>
        /// <value>The time to live. The default is InfiniteTimeSpan, meaning the cache entries never become stale.
        /// </value>
        public TimeSpan Ttl { get; init; } = Timeout.InfiniteTimeSpan;

        private int _cacheMaxSize = 100;
    }
}
