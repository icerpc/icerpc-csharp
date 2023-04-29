// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Default implementation for <see cref="IRequestContextFeature" />.</summary>
public sealed class RequestContextFeature : IRequestContextFeature
{
    /// <inheritdoc/>
    public IDictionary<string, string> Value { get; }

    /// <summary>Constructs an empty writeable request context feature.</summary>
    public RequestContextFeature() => Value = new Dictionary<string, string>();

    /// <summary>Constructs a request context feature with the specified dictionary.</summary>
    /// <param name="dictionary">The dictionary that the new feature will hold.</param>
    public RequestContextFeature(IDictionary<string, string> dictionary) => Value = dictionary;
}
