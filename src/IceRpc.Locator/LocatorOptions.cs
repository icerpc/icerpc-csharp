// Copyright (c) ZeroC, Inc.

namespace IceRpc.Locator;

/// <summary>A property bag used to configure a <see cref="LocatorLocationResolver" />.</summary>
public sealed record class LocatorOptions
{
    /// <summary>Gets or sets a value indicating whether or not the locator must enable background lookups.</summary>
    /// <value>When <see langword="true" />, if the lookup finds a stale cache entry, it returns the stale entry's
    /// server address(es) and executes a call "in the background" to refresh this entry. Defaults to <see
    /// langword="false" />, meaning the lookup does not return stale values.</value>
    public bool Background { get; set; }

    /// <summary>Gets or sets the maximum size of the cache.</summary>
    /// <value>The maximum size of the cache. <c>0</c> means no cache. Defaults to <c>100</c>.</value>
    public int MaxCacheSize { get; set; } = 100;

    /// <summary>Gets or sets the refresh threshold. When the age of a cache entry is less than or equal to this
    /// value, it's considered up to date and won't be updated even when the caller requests a refresh.</summary>
    /// <value>The refresh threshold. Defaults to <c>1</c> second.</value>
    public TimeSpan RefreshThreshold { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the time-to-live. This is the time period after which a cache entry is considered
    /// stale.</summary>
    /// <value>The time to live. Defaults to <see cref="Timeout.InfiniteTimeSpan" />, meaning the cache entries never
    /// become stale.
    /// </value>
    public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;
}
