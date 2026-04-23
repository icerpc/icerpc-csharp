// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Represents a feature that holds a <see cref="IDictionary{TKey, TValue}"/> of strings. This feature can be
/// transmitted as a <see cref="RequestFieldKey.Context" /> field with both ice and icerpc.</summary>
/// <remarks>The outgoing request context is encoded lazily, when the request is sent. To avoid racing mutations
/// with encoding, an interceptor that needs to add or remove context entries should build a new dictionary and
/// install a new <see cref="IRequestContextFeature"/>, rather than mutating <see cref="Value"/> in place.
/// </remarks>
/// <example>
/// The following code shows how to update the request context from an interceptor using copy-on-write.
/// <code source="../../../docfx/examples/IceRpc.RequestContext.Examples/RequestContextInterceptorExamples.cs"
///     region="UpdateRequestContextInInterceptor" lang="csharp" />
/// </example>
public interface IRequestContextFeature
{
    /// <summary>Gets the value of this feature.</summary>
    /// <value>The request context.</value>
    IDictionary<string, string> Value { get; }
}
