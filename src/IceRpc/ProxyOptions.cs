// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public class ProxyOptions
    {
        public static TimeSpan DefaultInvocationTimeout { get; } = TimeSpan.FromSeconds(60);

        /// <summary>Specifies whether or not the proxy caches its connection. The default value is true. This property
        /// is inherited when unmarshaling a proxy.</summary>
        public bool CacheConnection { get; set; } = true;

        /// <summary>The communicator (temporary). This property is inherited when unmarshaling a proxy.</summary>
        public Communicator? Communicator { get; set; }

        /// <summary>The connection cached by the proxy. Cannot be null when <see cref="IsFixed"/> is true. This
        /// property is not inherited when unmarshaling a proxy.</summary>
        public Connection? Connection { get; set; }

        /// <summary>The context of the proxy. This property is inherited when unmarshaling a proxy.</summary>
        public IReadOnlyDictionary<string, string> Context { get; set; } =
            ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>The encoding of the proxy. Its default value is the encoding of <see cref="Protocol"/>. This
        /// property is not inherited when unmarshaling a proxy because a marshaled proxy always specifies its
        /// encoding.</summary>
        public Encoding Encoding
        {
            get => _encoding ?? Protocol.GetEncoding();
            set => _encoding = value;
        }

        /// <summary>The endpoints of the proxy. This property is not inherited when unmarshaling a proxy.</summary>
        public IEnumerable<Endpoint> Endpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The invocation interceptors of the proxy. This property is inherited when unmarshaling a proxy.
        /// </summary>
        public IEnumerable<InvocationInterceptor> InvocationInterceptors { get; set; } =
            ImmutableList<InvocationInterceptor>.Empty;

        /// <summary>The invocation timeout of the proxy. This property is inherited when unmarshaling a proxy.
        /// </summary>
        public TimeSpan InvocationTimeout
        {
            get => _invocationTimeout;
            set => _invocationTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException("0 is not a valid value for the invocation timeout", nameof(value));
        }

        /// <summary>When true, the proxy is a "fixed" proxy bound to its connection. This property is not inherited
        /// when unmarshaling a proxy.</summary>
        public bool IsFixed { get; set; }

        /// <summary>When true, a void-returning operation on the proxy is invoked "oneway" even when no oneway metadata
        /// is specified. This property is inherited when unmarshaling a proxy.</summary>
        public bool IsOneway { get; set; }

        /// <summary>The location resolver of the proxy. This property is inherited when unmarshaling a proxy.</summary>
        public ILocationResolver? LocationResolver { get; set; }

        /// <summary>(temporary). This property is inherited when unmarshaling a proxy.</summary>
        public NonSecure NonSecure { get; set; } = NonSecure.Always;

        /// <summary>The path of the proxy. Its default value is the empty string. This property is not inherited when
        /// unmarshaling a proxy because a marshaled proxy always specifies its path.</summary>
        public string Path { get; set; } = "";

        /// <summary>(temporary). This property is inherited when unmarshaling a proxy.</summary>
        public bool PreferExistingConnection { get; set; } = true;

        /// <summary>The protocol of the proxy. Its default value is ice2. This property is not inherited when
        /// unmarshaling a proxy because a marshaled proxy always specifies its protocol.</summary>
        public Protocol Protocol { get; set; } = Protocol.Ice2;

        private Encoding? _encoding;
        private TimeSpan _invocationTimeout = DefaultInvocationTimeout;

        public ProxyOptions Clone() => (ProxyOptions)MemberwiseClone();

        /// <summary>Returns a copy of this options instance with all its inheritable properties. Non-inheritable
        /// properties are set to the value of the corresponding parameters or to their default values.</summary>
        internal ProxyOptions With(
            Encoding encoding,
            IEnumerable<Endpoint> endpoints,
            string path,
            Protocol protocol) =>
            new()
            {
                CacheConnection = CacheConnection,
                Communicator = Communicator,
                // Connection remains null
                Context = Context,
                Encoding = encoding,
                Endpoints = endpoints,
                InvocationInterceptors = InvocationInterceptors,
                InvocationTimeout = InvocationTimeout,
                // IsFixed remains false
                IsOneway = IsOneway,
                LocationResolver = LocationResolver,
                NonSecure = NonSecure,
                Path = path,
                PreferExistingConnection = PreferExistingConnection,
                Protocol = protocol
            };

        /// <summary>Returns a copy of this options instance with all its inheritable properties. Non-inheritable
        /// properties are set using the supplied connection and path, or to their default values.</summary>
        internal ProxyOptions With(Connection fixedConnection, string path) =>
            new()
            {
                CacheConnection = CacheConnection,
                Communicator = Communicator,
                Connection = fixedConnection,
                Context = Context,
                Encoding = fixedConnection.Protocol.GetEncoding(),
                Endpoints = ImmutableList<Endpoint>.Empty,
                InvocationInterceptors = InvocationInterceptors,
                InvocationTimeout = InvocationTimeout,
                IsFixed = true,
                IsOneway = IsOneway,
                LocationResolver = LocationResolver,
                NonSecure = NonSecure,
                Path = path,
                PreferExistingConnection = PreferExistingConnection,
                Protocol = fixedConnection.Protocol
            };
    }
}
