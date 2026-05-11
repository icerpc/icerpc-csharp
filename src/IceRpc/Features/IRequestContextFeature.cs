// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Represents a feature that holds a read-only dictionary of strings. This feature can be transmitted as a
/// <see cref="RequestFieldKey.Context" /> field with both ice and icerpc.</summary>
/// <remarks>An interceptor that needs to add or remove context entries should build a new dictionary and install a
/// new <see cref="IRequestContextFeature"/>.</remarks>
/// <example>
/// The following code shows how to update the request context from an interceptor.
/// <code source="../../../docfx/examples/IceRpc.RequestContext.Examples/RequestContextInterceptorExamples.cs"
///     region="UpdateRequestContextInInterceptor" lang="csharp" />
/// </example>
public interface IRequestContextFeature
{
    /// <summary>Gets the value of this feature.</summary>
    /// <value>The request context.</value>
    IReadOnlyDictionary<string, string> Value { get; }
}
