// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="IRequestContextFeature" />.</summary>
public sealed class RequestContextFeature : IRequestContextFeature
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Value { get; }

    /// <summary>Constructs an empty request context feature.</summary>
    public RequestContextFeature() => Value = ImmutableDictionary<string, string>.Empty;

    /// <summary>Constructs a request context feature wrapping the specified dictionary.</summary>
    /// <param name="value">The read-only dictionary held by the new feature.</param>
    public RequestContextFeature(IReadOnlyDictionary<string, string> value) => Value = value;
}
