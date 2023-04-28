// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>Default implementation for <see cref="IRequestContextFeature" />.</summary>
public sealed class RequestContextFeature : IRequestContextFeature
{
    /// <inheritdoc/>
    public IDictionary<string, string> Value { get; set; } = ImmutableSortedDictionary<string, string>.Empty;
}
