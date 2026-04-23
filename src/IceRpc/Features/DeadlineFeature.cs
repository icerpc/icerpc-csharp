// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="IDeadlineFeature" />.</summary>
public sealed class DeadlineFeature : IDeadlineFeature
{
    // The maximum delay CancellationTokenSource.CancelAfter(TimeSpan) accepts.
    private static readonly TimeSpan MaxCancelAfterDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>Creates a deadline from a timeout.</summary>
    /// <param name="timeout">The timeout. Must be positive and must not exceed
    /// <see cref="int.MaxValue" /> milliseconds (~24.8 days), the maximum supported by
    /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)" />.</param>
    /// <returns>A new deadline equal to now plus the timeout.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="timeout" /> is not positive, or exceeds the
    /// maximum supported value.</exception>
    public static DeadlineFeature FromTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"The {nameof(timeout)} value must be positive.",
                nameof(timeout));
        }
        if (timeout > MaxCancelAfterDelay)
        {
            throw new ArgumentException(
                $"The {nameof(timeout)} value must not exceed {MaxCancelAfterDelay}.",
                nameof(timeout));
        }
        return new(DateTime.UtcNow + timeout);
    }

    /// <inheritdoc/>
    public DateTime Value { get; }

    /// <summary>Constructs a deadline feature.</summary>
    /// <param name="deadline">The deadline value.</param>
    public DeadlineFeature(DateTime deadline) => Value = deadline;
}
