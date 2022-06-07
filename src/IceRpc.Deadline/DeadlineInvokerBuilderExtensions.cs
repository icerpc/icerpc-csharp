// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deadline;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the deadline interceptor to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class DeadlineInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="DeadlineInterceptor"/> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseDeadline(this IInvokerBuilder builder) =>
        builder.Use(next => new DeadlineInterceptor(next, Timeout.InfiniteTimeSpan));

    /// <summary>Adds a <see cref="DeadlineInterceptor"/> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="timeout">The default timeout for the request. This value can be overwritten by setting the
    /// <see cref="ITimeoutFeature"/> request feature.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseDeadline(this IInvokerBuilder builder, TimeSpan timeout) =>
        builder.Use(next => new DeadlineInterceptor(next, timeout));
}
