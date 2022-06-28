// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deadline;

namespace IceRpc;

/// <summary>This class provides extension methods to add the deadline interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class DeadlinePipelineExtensions
{
    /// <summary>Adds a <see cref="DeadlineInterceptor"/> with an infinite default timeout to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseDeadline(this Pipeline pipeline) => pipeline.UseDeadline(Timeout.InfiniteTimeSpan);

    /// <summary>Adds a <see cref="DeadlineInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="defaultTimeout">The deadline interceptor options.</param>
    /// <param name="alwaysEnforceDeadline">When <c>true</c> and the request carries a deadline, the interceptor always
    /// creates a cancellation token source to enforce this deadline. When <c>false</c> and the request carries a
    /// deadline, the interceptor creates a cancellation token source to enforce this deadline only when the
    /// invocation's cancellation token cannot be canceled. The default value is <c>false</c>.</param>
    /// <returns>The builder being configured.</returns>
    public static Pipeline UseDeadline(
        this Pipeline pipeline,
        TimeSpan defaultTimeout,
        bool alwaysEnforceDeadline = false) =>
        pipeline.Use(next => new DeadlineInterceptor(next, defaultTimeout, alwaysEnforceDeadline));
}
