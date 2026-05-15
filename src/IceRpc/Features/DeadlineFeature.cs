// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="IDeadlineFeature" />.</summary>
public sealed class DeadlineFeature : IDeadlineFeature
{
    // The maximum supported timeout (int.MaxValue ms, ~24.8 days). This is the maximum delay
    // CancellationTokenSource.CancelAfter accepts.
    private static readonly TimeSpan MaxSupportedTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>Creates a deadline from a timeout.</summary>
    /// <param name="timeout">The timeout. Must be a positive value not exceeding ~24.8 days.</param>
    /// <returns>A new deadline equal to now plus the timeout.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="timeout" /> is not a positive value
    /// within the supported range.</exception>
    public static DeadlineFeature FromTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaxSupportedTimeout)
        {
            throw new ArgumentException(
                $"The {nameof(timeout)} value must be positive and not exceed {MaxSupportedTimeout}.",
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
