// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc;

/// <summary>Provides an extension method to add a request context middleware to a <see cref="Router" />.</summary>
public static class RequestContextRouterExtensions
{
    /// <summary>Adds a <see cref="RequestContextMiddleware" /> to this router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    /// <example>
    /// The following code adds the request context middleware to the dispatch pipeline.
    /// <code source="../../docfx/examples/IceRpc.RequestContext.Examples/RequestContextMiddlewareExamples.cs" region="UseRequestContext" lang="csharp" />
    /// The greeter service can then access the request context from the features parameter as shown in the following
    /// code.
    /// <code source="../../docfx/examples/IceRpc.RequestContext.Examples/Chatbot.cs" region="RequestContextFeature" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/RequestContext"/>
    public static Router UseRequestContext(this Router router) =>
        router.Use(next => new RequestContextMiddleware(next));
}
