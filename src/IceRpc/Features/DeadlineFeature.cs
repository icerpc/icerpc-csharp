// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="IDeadlineFeature" />.</summary>
public sealed class DeadlineFeature : IDeadlineFeature
{
    /// <summary>Creates a deadline from a timeout.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <returns>A new deadline equal to now plus the timeout.</returns>
    public static DeadlineFeature FromTimeout(TimeSpan timeout) => new(DateTime.UtcNow + timeout);

    /// <inheritdoc/>
    public DateTime Value { get; }

    /// <summary>Constructs a deadline feature.</summary>
    /// <param name="deadline">The deadline value. Must have <see cref="DateTime.Kind" /> equal to
    /// <see cref="DateTimeKind.Utc" />, or be <see cref="DateTime.MaxValue" /> (no deadline).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="deadline" /> is not UTC and is not
    /// <see cref="DateTime.MaxValue" />.</exception>
    public DeadlineFeature(DateTime deadline)
    {
        if (deadline != DateTime.MaxValue && deadline.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                $"The deadline must have {nameof(DateTimeKind)}.{nameof(DateTimeKind.Utc)}.",
                nameof(deadline));
        }
        Value = deadline;
    }
}
