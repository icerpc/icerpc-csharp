// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Deadline;

/// <summary>The default implementation of <see cref="IDeadlineFeature"/>.</summary>
public sealed class DeadlineFeature : IDeadlineFeature
{
    /// <summary>Creates a deadline from a timeout.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <returns>A new deadline equal to now plus the timeout.</returns>
    public static DeadlineFeature FromTimeout(TimeSpan timeout) => new() { Value = DateTime.UtcNow + timeout };

    /// <inheritdoc/>
    public DateTime Value { get; init; } = DateTime.MaxValue;
}
