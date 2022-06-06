// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deadline;

namespace IceRpc;

/// <summary>This class provides extension methods to add the deadline interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class DeadlinePipelineExtensions
{
    /// <summary>Adds a <see cref="DeadlineInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseDeadline(this Pipeline pipeline) =>
        pipeline.Use(next => new DeadlineInterceptor(next, Timeout.InfiniteTimeSpan));

    /// <summary>Adds a <see cref="DeadlineInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="timeout">The default timeout for the request. This value can be overwritten by setting the
    /// <see cref="ITimeoutFeature"/> request feature.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseDeadline(this Pipeline pipeline, TimeSpan timeout) =>
        pipeline.Use(next => new DeadlineInterceptor(next, timeout));
}
