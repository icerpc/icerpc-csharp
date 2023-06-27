// Copyright (c) ZeroC, Inc.

using IceRpc.Deadline;

namespace IceRpc;

/// <summary>Provides extension methods to add the deadline interceptor to a <see cref="Pipeline" />.</summary>
public static class DeadlinePipelineExtensions
{
    /// <summary>Adds a <see cref="DeadlineInterceptor" /> with an infinite default timeout to this pipeline.
    /// This interceptor enforces the deadlines it receives and does not create new deadlines.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    /// <example>
    /// The following code adds the deadline interceptor to the invocation pipeline.
    /// <code source="../../docfx/examples/IceRpc.Deadline.Examples/DeadlineInterceptorExamples.cs" region="UseDeadline" lang="csharp" />
    /// </example>
    public static Pipeline UseDeadline(this Pipeline pipeline) => pipeline.UseDeadline(Timeout.InfiniteTimeSpan);

    /// <summary>Adds a <see cref="DeadlineInterceptor" /> to this pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="defaultTimeout">The default timeout. When not infinite, the interceptor adds a deadline to requests
    /// without a deadline.</param>
    /// <param name="alwaysEnforceDeadline">When <see langword="true" /> and the request carries a deadline, the
    /// interceptor always creates a cancellation token source to enforce this deadline. When <see langword="false" />
    /// and the request carries a deadline, the interceptor creates a cancellation token source to enforce this deadline
    /// only when the invocation's cancellation token cannot be canceled. The default value is <see langword="false" />.
    /// </param>
    /// <returns>The pipeline being configured.</returns>
    /// <example>
    /// The following code adds the deadline interceptor to the invocation pipeline.
    /// <code source="../../docfx/examples/IceRpc.Deadline.Examples/DeadlineInterceptorExamples.cs" region="UseDeadlineWithDefaultTimeout" lang="csharp" />
    /// </example>
    public static Pipeline UseDeadline(
        this Pipeline pipeline,
        TimeSpan defaultTimeout,
        bool alwaysEnforceDeadline = false) =>
        pipeline.Use(next => new DeadlineInterceptor(next, defaultTimeout, alwaysEnforceDeadline));
}
