// Copyright (c) ZeroC, Inc.

using IceRpc.Builder;
using IceRpc.Deadline;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method to add the Deadline middleware to a <see cref="IDispatcherBuilder" />.
/// </summary>
public static class DeadlineDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="DeadlineMiddleware" /> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseDeadline(this IDispatcherBuilder builder) =>
        builder.Use(next => new DeadlineMiddleware(next));
}
