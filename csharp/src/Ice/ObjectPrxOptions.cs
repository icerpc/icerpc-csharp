// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;

namespace ZeroC.Ice
{
    /// <summary>Publicly visible Ice-internal struct used for the construction of ObjectPrx and derived classes.
    /// Applications should not (and cannot) use it directly.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly ref struct ObjectPrxOptions
    {
        internal readonly bool CacheConnection;
        internal readonly Communicator Communicator;

        internal readonly Connection? Connection;

        internal readonly IReadOnlyDictionary<string, string> Context;
        internal readonly Identity Identity;

        internal readonly Encoding Encoding;
        internal readonly IReadOnlyList<Endpoint> Endpoints;

        internal readonly string Facet;

        internal readonly IReadOnlyList<InvocationInterceptor> InvocationInterceptors;

        internal readonly TimeSpan? InvocationTimeoutOverride;

        internal readonly bool IsOneway;

        internal readonly bool IsRelative;

        internal readonly object? Label;

        internal readonly IReadOnlyList<string> Location;

        internal readonly ILocationResolver? LocationResolver;
        internal readonly bool? PreferExistingConnectionOverride;

        internal readonly NonSecure? PreferNonSecureOverride;
        internal readonly Protocol Protocol;

        internal ObjectPrxOptions(
            Communicator communicator,
            Identity identity,
            Protocol protocol,
            bool cacheConnection = true,
            IReadOnlyDictionary<string, string>? context = null,
            Encoding? encoding = null,
            IReadOnlyList<Endpoint>? endpoints = null,
            string facet = "",
            Connection? fixedConnection = null,
            IReadOnlyList<InvocationInterceptor>? invocationInterceptors = null,
            TimeSpan? invocationTimeout = null,
            object? label = null,
            IReadOnlyList<string>? location = null,
            ILocationResolver? locationResolver = null,
            bool oneway = false,
            bool? preferExistingConnection = null,
            NonSecure? preferNonSecure = null,
            bool relative = false)
        {
            CacheConnection = cacheConnection;
            Communicator = communicator;
            Connection = fixedConnection;
            Context = context ?? communicator.DefaultContext;
            Encoding = encoding ?? protocol.GetEncoding();
            Endpoints = endpoints ?? ImmutableList<Endpoint>.Empty;
            Facet = facet;
            Identity = identity;
            InvocationInterceptors = invocationInterceptors ?? communicator.DefaultInvocationInterceptors;
            InvocationTimeoutOverride = invocationTimeout;
            IsOneway = oneway;
            IsRelative = relative;
            Label = label;
            Location = location ?? ImmutableList<string>.Empty;
            LocationResolver = locationResolver;
            PreferExistingConnectionOverride = preferExistingConnection;
            PreferNonSecureOverride = preferNonSecure;
            Protocol = protocol;
        }
    }
}
