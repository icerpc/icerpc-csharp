// Copyright (c) ZeroC, Inc.

using IceRpc.Deadline;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Router" /> to add the deadline middleware.</summary>
public static class DeadlineRouterExtensions
{
    /// <summary>Extension methods for <see cref="Router" />.</summary>
    /// <param name="router">The router being configured.</param>
    extension(Router router)
    {
        /// <summary>Adds a <see cref="DeadlineMiddleware" /> to this router.</summary>
        /// <returns>The router being configured.</returns>
        /// <example>
        /// The following code adds the deadline middleware to the dispatch pipeline.
        /// <code source="../../docfx/examples/IceRpc.Deadline.Examples/DeadlineMiddlewareExamples.cs"
        /// region="UseDeadline" lang="csharp" />
        /// </example>
        public Router UseDeadline() => router.Use(next => new DeadlineMiddleware(next));
    }
}
