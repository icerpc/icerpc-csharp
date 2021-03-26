// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;

namespace IceRpc.Interop
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public sealed class InteropProxyOptions : ProxyOptions
    {
        /// <summary>The facet of the proxy. Its default value is the empty string. This property is not inherited when
        /// unmarshaling a proxy because a marshaled ice1 proxy always specifies its facet.</summary>
        public string Facet { get; set; } = "";

        /// <summary>The identity of the proxy. This property is not inherited when unmarshaling a proxy because a
        /// marshaled ice1 proxy always specifies its identity.</summary>
        public Identity Identity { get; set; } = Identity.Empty;

        public InteropProxyOptions()
        {
            Protocol = Protocol.Ice1;
        }
    }

    internal static class ProxyOptionsExtensions
    {
        /// <summary>Returns a copy of this options instance with all its inheritable properties. Non-inheritable
        /// properties are set to the value of the corresponding parameters or to their default values.</summary>
        internal static InteropProxyOptions With(
            this ProxyOptions options,
            Encoding encoding,
            IEnumerable<Endpoint> endpoints,
            string facet,
            Identity identity,
            bool oneway) =>
            new()
            {
                CacheConnection = options.CacheConnection,
                Communicator = options.Communicator,
                // Connection remains null
                Context = options.Context,
                Encoding = encoding,
                Endpoints = endpoints,
                Facet = facet,
                Identity = identity,
                InvocationInterceptors = options.InvocationInterceptors,
                InvocationTimeout = options.InvocationTimeout,
                // IsFixed remains false
                IsOneway = oneway,
                Label = options.Label,
                LocationResolver = options.LocationResolver,
                NonSecure = options.NonSecure,
                // Path remains empty
                PreferExistingConnection = options.PreferExistingConnection,
                Protocol = Protocol.Ice1
            };
    }
}
