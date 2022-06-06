// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deadline;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the deadline middleware to a <see cref="IDispatcherBuilder"/>.
/// </summary>
public static class DeadlineDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="DeadlineMiddleware"/> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseDeadline(this IDispatcherBuilder builder) =>
        builder.Use(next => new DeadlineMiddleware(next));
}
