// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Configure
{
    /// <summary>An options class for configuring a <see cref="LocatorLocationResolver"/>.</summary>
    /// <seealso cref="InteropPipelineExtensions.UseLocator(Pipeline, LocatorOptions)"/>
    public sealed record class LocatorOptions
    {
        /// <summary>Gets or sets the background configuration.</summary>
        /// <value>When <c>true</c>, if the lookup finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is <c>false</c>, meaning the
        /// lookup does not return stale values.</value>
        public bool Background { get; set; }

        /// <summary>Gets or sets the maximum size of the cache.</summary>
        /// <value>The maximum size of the cache. 0 means no cache. The default is 100.</value>
        public int CacheMaxSize
        {
            get => _cacheMaxSize;
            set => _cacheMaxSize = value >= 0 ? value :
                throw new ArgumentException($"{nameof(CacheMaxSize)} must be positive", nameof(value));
        }

        /// <summary>Gets or sets the locator proxy.</summary>
        public ILocatorPrx? Locator { get; set; }

        /// <summary>Gets or sets the logger factory used to create the IceRpc logger.</summary>
        public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

        /// <summary>Gets or sets the refresh threshold. When the age of a cache entry is <see cref="RefreshThreshold"/>
        /// or less, it's considered up to date and won't be updated even when the caller requests a refresh.</summary>
        /// <value>The refresh threshold. The default is 1 second.</value>
        public TimeSpan RefreshThreshold { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Gets or sets the time-to-live. This is the time period after which a cache entry is considered
        /// stale.</summary>
        /// <value>The time to live. The default is InfiniteTimeSpan, meaning the cache entries never become stale.
        /// </value>
        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;

        private int _cacheMaxSize = 100;
    }
}
