// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public class ServicePrxOptions
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
        public IReadOnlyDictionary<string, string>? Context { get; set; }

        /// <summary>The encoding of the proxy. Its default value is the encoding of <see cref="Protocol"/>. This
        /// property is not inherited when unmarshaling a proxy because a marshaled proxy always specifies its
        /// encoding.</summary>
        public Encoding Encoding
        {
            get => _encoding ?? Protocol.GetEncoding();
            set => _encoding = value;
        }

        /// <summary>The endpoints of the proxy. This property is not inherited when unmarshaling a proxy.</summary>
        public IReadOnlyList<Endpoint> Endpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The invocation interceptors of the proxy. This property is inherited when unmarshaling a proxy.
        /// </summary>
        public IReadOnlyList<InvocationInterceptor>? InvocationInterceptors { get; set; }

        /// <summary>The invocation timeout of the proxy. This property is inherited when unmarshaling a proxy.
        /// </summary>
        public TimeSpan InvocationTimeout { get; set; } = DefaultInvocationTimeout;

        /// <summary>When true, the proxy is a "fixed" proxy bound to its connection. This property is not inherited
        /// when unmarshaling a proxy.</summary>
        public bool IsFixed { get; set; }

        /// <summary>When true, a void-returning operation on the proxy is invoked "oneway" even when no oneway metadata
        /// is specified. This property is not inherited when unmarshaling a proxy.</summary>
        public bool IsOneway { get; set; }

        /// <summary>The label of the proxy. This property is inherited when unmarshaling a proxy.</summary>
        public object? Label { get; set; }

        /// <summary>The location resolver of the proxy. This property is inherited when unmarshaling a proxy.</summary>
        public ILocationResolver? LocationResolver { get; set; }

        /// <summary>The path of the proxy. Its default value is the empty string. This property is not inherited when
        /// unmarshaling a proxy because a marshaled proxy always specifies its path.</summary>
        public string Path { get; set; } = "";

        /// <summary>(temporary). This property is inherited when unmarshaling a proxy.</summary>
        public bool PreferExistingConnection { get; set; } = true;

        /// <summary>(temporary). This property is inherited when unmarshaling a proxy.</summary>
        public NonSecure PreferNonSecure { get; set; } = NonSecure.Always;

        /// <summary>The protocol of the proxy. Its default value is ice2. This property is not inherited when
        /// unmarshaling a proxy because a marshaled proxy always specifies its protocol.</summary>
        public Protocol Protocol { get; set; } = Protocol.Ice2;

        private Encoding? _encoding;
    }
}
