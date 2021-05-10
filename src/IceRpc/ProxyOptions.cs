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

        /// <summary>The context of the proxy.</summary>
        public IReadOnlyDictionary<string, string> Context { get; set; } =
            ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>The invocation timeout of the proxy.</summary>
        public TimeSpan InvocationTimeout
        {
            get => _invocationTimeout;
            set => _invocationTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException("0 is not a valid value for the invocation timeout", nameof(value));
        }

        /// <summary>The invoker.</summary>
        public IInvoker? Invoker { get; set; }

        /// <summary>When true, a void-returning operation on the proxy is invoked "oneway" even when no oneway metadata
        /// is specified.</summary>
        public bool IsOneway { get; set; }

        private TimeSpan _invocationTimeout = DefaultInvocationTimeout;

        public ProxyOptions Clone() => (ProxyOptions)MemberwiseClone();
    }
}
