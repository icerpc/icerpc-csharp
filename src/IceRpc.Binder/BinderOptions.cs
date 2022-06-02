// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Binder;

/// <summary>An options class for configuring a <see cref="BinderInterceptor"/>.</summary>
public sealed record class BinderOptions
{
    /// <summary>Gets or sets a value indicating whether the binder stores the connection it retrieves from its client
    /// connection provider in the proxy that created the request. The default is <c>true</c>.</summary>
    public bool CacheConnection { get; set; } = true;
}
