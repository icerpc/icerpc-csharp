// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>). All these
    /// properties are inherited when unmarshaling a proxy.</summary>
    public class ProxyOptions
    {
        public static TimeSpan DefaultInvocationTimeout { get; } = TimeSpan.FromSeconds(60);

        /// <summary>Specifies whether or not the proxy caches its connection. The default value is true.</summary>
        public bool CacheConnection { get; set; } = true;

        /// <summary>The communicator (temporary).</summary>
        public Communicator? Communicator { get; set; }

        /// <summary>The context of the proxy.</summary>
        public IReadOnlyDictionary<string, string> Context { get; set; } =
            ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>The invocation interceptors of the proxy.</summary>
        public IEnumerable<InvocationInterceptor> InvocationInterceptors { get; set; } =
            ImmutableList<InvocationInterceptor>.Empty;

        /// <summary>The invocation timeout of the proxy.</summary>
        public TimeSpan InvocationTimeout
        {
            get => _invocationTimeout;
            set => _invocationTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException("0 is not a valid value for the invocation timeout", nameof(value));
        }

        /// <summary>When true, a void-returning operation on the proxy is invoked "oneway" even when no oneway metadata
        /// is specified.</summary>
        public bool IsOneway { get; set; }

        /// <summary>The location resolver of the proxy.</summary>
        public ILocationResolver? LocationResolver { get; set; }

        /// <summary>(temporary).</summary>
        public bool PreferExistingConnection { get; set; } = true;

        private TimeSpan _invocationTimeout = DefaultInvocationTimeout;

        public ProxyOptions Clone() => (ProxyOptions)MemberwiseClone();
    }
}
