// Copyright (c) ZeroC, Inc.

using IceRpc.Deadline;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the deadline
/// middleware.</summary>
public static class DeadlineDispatcherBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IDispatcherBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IDispatcherBuilder builder)
    {
        /// <summary>Adds a <see cref="DeadlineMiddleware" /> to this dispatcher builder.</summary>
        /// <returns>The builder being configured.</returns>
        public IDispatcherBuilder UseDeadline() =>
            builder.Use(next => new DeadlineMiddleware(next));
    }
}
