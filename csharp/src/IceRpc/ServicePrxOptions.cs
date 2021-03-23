// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public class ServicePrxOptions
    {
        public bool CacheConnection { get; set; } = true;
        public Communicator? Communicator { get; set; }

        public Connection? Connection { get; set; }

        public IReadOnlyDictionary<string, string>? Context { get; set; }

        public Encoding? Encoding { get; set; }
        public IReadOnlyList<Endpoint> Endpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        public IReadOnlyList<InvocationInterceptor>? InvocationInterceptors { get; set; }

        public TimeSpan? InvocationTimeoutOverride { get; set; }

        public bool IsFixed { get; set; }

        public bool IsOneway { get; set; }

        public object? Label { get; set; }

        public ILocationResolver? LocationResolver { get; set; }

        public string Path { get; set; } = ""; // Path and Identity can't be set at the same time

        public bool? PreferExistingConnectionOverride { get; set; }

        public NonSecure? PreferNonSecureOverride { get; set; }
        public Protocol Protocol { get; set; } = Protocol.Ice2;
    }
}
