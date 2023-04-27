// Copyright (c) ZeroC, Inc.

using IceRpc.Deadline;

namespace IceRpc.Builder;

/// <summary>Provides extension methods to add the Deadline interceptor to an <see cref="IInvokerBuilder" />.
/// </summary>
public static class DeadlineInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="DeadlineInterceptor" /> with an infinite default timeout to this invoker builder.
    /// </summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseDeadline(this IInvokerBuilder builder) =>
        builder.UseDeadline(Timeout.InfiniteTimeSpan);

    /// <summary>Adds a <see cref="DeadlineInterceptor" /> to this invoker builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="defaultTimeout">The default timeout. When infinite, the interceptor enforces the deadlines it
    /// receives and does not create new deadlines.</param>
    /// <param name="alwaysEnforceDeadline">When <see langword="true" /> and the request carries a deadline, the
    /// interceptor always creates a cancellation token source to enforce this deadline. When <see langword="false" />
    /// and the request carries a deadline, the interceptor creates a cancellation token source to enforce this deadline
    /// only when the invocation's cancellation token cannot be canceled. The default value is <see langword="false" />.
    /// </param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseDeadline(
        this IInvokerBuilder builder,
        TimeSpan defaultTimeout,
        bool alwaysEnforceDeadline = false) =>
        builder.Use(next => new DeadlineInterceptor(next, defaultTimeout, alwaysEnforceDeadline));
}
