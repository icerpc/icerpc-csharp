// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Configure
{
    /// <summary>An options class for configuring a Locator interceptor.</summary>
    public sealed record class LocatorOptions
    {
        /// <summary>When true, if the lookup finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is false, meaning the lookup
        /// does not return stale values.</summary>
        public bool Background { get; init; }

        /// <summary>The maximum size of the cache. Must be 0 (meaning no cache) or greater. The default value is
        /// 100.</summary>
        public int CacheMaxSize
        {
            get => _cacheMaxSize;
            init => _cacheMaxSize = value >= 0 ? value :
                throw new ArgumentException($"{nameof(CacheMaxSize)} must be positive", nameof(value));
        }

        /// <summary>When a cache entry's age is <c>JustRefreshedAge</c> or less, it's considered just refreshed and
        /// won't be updated even when the caller requests a refresh.</summary>
        public TimeSpan JustRefreshedAge { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>The logger factory used to create the IceRpc logger.</summary>
        public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

        /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning
        /// the cache entries never become stale.</summary>
        public TimeSpan Ttl { get; init; } = Timeout.InfiniteTimeSpan;

        private int _cacheMaxSize = 100;
    }
}
