// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>An options class for configuring a Locator interceptor.</summary>
        public sealed class LocatorOptions
        {
            /// <summary>When true, if the lookup finds a stale cache entry, it returns the stale entry's endpoint(s)
            /// and executes a call "in the background" to refresh this entry. The default is false, meaning the lookup
            /// does not return stale values.</summary>
            public bool Background { get; set; }

            /// <summary>The maximum size of the cache. Must be 0 (meaning no cache) or greater.</summary>
            public int CacheMaxSize
            {
                get => _cacheMaxSize;
                set => _cacheMaxSize = value >= 0 ? value :
                    throw new ArgumentException($"{nameof(CacheMaxSize)} must be positive", nameof(value));
            }

            /// <summary>When a cache entry's age is <c>JustRefreshedAge</c> or less, it's considered just refreshed and
            /// won't be updated even when the caller requests a refresh.</summary>
            public TimeSpan JustRefreshedAge { get; set; } = TimeSpan.FromSeconds(1);

            /// <summary>The logger factory used to create the IceRpc logger.</summary>
            public ILoggerFactory LoggerFactory { get; set; } = Runtime.DefaultLoggerFactory;

            /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning
            /// the cache entries never become stale.</summary>
            public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;

            private int _cacheMaxSize = 100;
        }

        /// <summary>Creates a location resolver interceptor that resolves loc endpoints using an <see cref="ILocator"/>
        /// service and the default for <see cref="LocatorOptions"/>.</summary>
        /// <param name="locator">A proxy to the locator service.</param>
        /// <returns>The new location resolver interceptor.</returns>
        public static Func<ILocationResolver, ILocationResolver> Locator(ILocatorPrx locator) =>
            Locator(locator, new());

        /// <summary>Creates a location resolver interceptor that resolves loc endpoints using an <see cref="ILocator"/>
        /// service.</summary>
        /// <param name="locator">A proxy to the locator service.</param>
        /// <param name="options">Options for this interceptor.</param>
        /// <returns>The new location resolver interceptor.</returns>
        public static Func<ILocationResolver, ILocationResolver> Locator(ILocatorPrx locator, LocatorOptions options) =>
            next => new LocatorClient(locator, next, options);
    }
}
