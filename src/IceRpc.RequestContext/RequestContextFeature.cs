// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.RequestContext;

/// <summary>The default implementation of <see cref="IRequestContextFeature" />.</summary>
public sealed class RequestContextFeature : IRequestContextFeature
{
    /// <inheritdoc/>
    public IDictionary<string, string> Value { get; set; } = ImmutableSortedDictionary<string, string>.Empty;
}
