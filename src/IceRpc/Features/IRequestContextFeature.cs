// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>A feature that holds a <see cref="IDictionary{TKey, TValue}"/> of strings. This feature can be transmitted
/// as a <see cref="RequestFieldKey.Context" /> field with both ice and icerpc.</summary>
public interface IRequestContextFeature
{
    /// <summary>Gets or sets the value of this feature.</summary>
    /// <value>The request context.</value>
    IDictionary<string, string> Value { get; set; }
}
