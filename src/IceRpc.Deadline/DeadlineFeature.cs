// Copyright (c) ZeroC, Inc.

namespace IceRpc.Deadline;

/// <summary>The default implementation of <see cref="IDeadlineFeature" />.</summary>
public sealed class DeadlineFeature : IDeadlineFeature
{
    /// <summary>Creates a deadline from a timeout.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <returns>A new deadline equal to now plus the timeout.</returns>
    public static DeadlineFeature FromTimeout(TimeSpan timeout) => new(DateTime.UtcNow + timeout);

    /// <inheritdoc/>
    public DateTime Value { get; }

    /// <summary>Constructs a deadline feature.</summary>
    /// <param name="deadline">The deadline value.</param>
    public DeadlineFeature(DateTime deadline) => Value = deadline;
}
