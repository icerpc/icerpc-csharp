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
    /// <param name="value">The dictionary held by the new feature. It is copied to an
    /// <see cref="ImmutableDictionary{TKey, TValue}" /> unless it is already one, so post-construction mutations
    /// to <paramref name="value" /> (and casts of the feature's <c>Value</c> back to a mutable dictionary) cannot
    /// alter the feature.</param>
    public RequestContextFeature(IReadOnlyDictionary<string, string> value) => Value = value.ToImmutableDictionary();
}
