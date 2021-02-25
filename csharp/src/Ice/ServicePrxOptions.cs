// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroC.Ice
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public sealed class ServicePrxOptions
    {
        public bool CacheConnection { get; set; } = true;
        public Communicator? Communicator { get; set; }

        public Connection? Connection { get; set; }

        public IReadOnlyDictionary<string, string>? Context { get; set; }
        public Identity Identity { get; set; }

        public Encoding? Encoding { get; set; }
        public IReadOnlyList<Endpoint> Endpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        public string Facet { get; set; } = "";

        public IReadOnlyList<InvocationInterceptor>? InvocationInterceptors { get; set; }

        public TimeSpan? InvocationTimeoutOverride { get; set; }

        public bool IsOneway { get; set; }

        public object? Label { get; set; }

        public IReadOnlyList<string> Location { get; set; } = ImmutableList<string>.Empty;

        public ILocationService? LocationService { get; set; }
        public bool? PreferExistingConnectionOverride { get; set; }

        public NonSecure? PreferNonSecureOverride { get; set; }
        public Protocol Protocol { get; set; } = Protocol.Ice2;
    }
}
