// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Deadline;

/// <summary>An options class for configuring the deadline interceptor.</summary>
public sealed record class DeadlineInterceptorOptions
{
    /// <summary>Gets or sets a value indicating whether the interceptor always enforces the deadline.</summary>
    /// <value>When <c>true</c> and the request carries a deadline, the interceptor always creates a cancellation token
    /// source to enforce this deadline. When <c>false</c> and the request carries a deadline, the interceptor creates a
    /// cancellation token source to enforce this deadline only when the invocation's cancellation token cannot be
    /// canceled. The default value is <c>false</c>.</value>
    /// <remarks>When the request does not carry a deadline, the interceptor always creates a new deadline using
    /// <see cref="DefaultTimeout"/> and creates a cancellation token source for this timeout.</remarks>
    public bool AlwaysEnforceDeadline { get; set; }

    /// <summary>Gets or sets the default timeout. When a request does not carry a deadline, the interceptor creates a
    /// deadline equal to now plus the default timeout, and enforces this deadline.</summary>
    /// <value>The default timeout. Its default value is infinite (no timeout).</value>
    public TimeSpan DefaultTimeout
    {
        get => _defaultTimeout;

        set
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentException($"{nameof(DefaultTimeout)} must be greater than 0", nameof(value));
            }
            _defaultTimeout = value;
        }
    }

    private TimeSpan _defaultTimeout = Timeout.InfiniteTimeSpan;
}
