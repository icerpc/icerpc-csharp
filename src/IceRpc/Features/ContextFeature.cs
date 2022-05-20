// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IContextFeature"/>.</summary>
public sealed class ContextFeature : IContextFeature
{
    /// <inheritdoc/>
    public IDictionary<string, string> Value { get; set; } = ImmutableSortedDictionary<string, string>.Empty;
}
