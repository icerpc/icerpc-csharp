// Copyright (c) ZeroC, Inc.

using IceRpc.Deadline;

namespace IceRpc;

/// <summary>Provides an extension method to add the Deadline middleware to a <see cref="Router" />.</summary>
public static class DeadlineRouterExtensions
{
    /// <summary>Adds a <see cref="DeadlineMiddleware" /> to this router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseDeadline(this Router router) => router.Use(next => new DeadlineMiddleware(next));
}
