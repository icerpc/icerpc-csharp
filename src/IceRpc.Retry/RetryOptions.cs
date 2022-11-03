// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Retry;

/// <summary>Options class to configure <see cref="RetryInterceptor" />.</summary>
public sealed record class RetryOptions
{
    /// <summary>Gets or sets the maximum number of attempts for retrying a request.</summary>
    /// <value>The maximum number of attempts for retrying a request. The default value is 2 attempts.</value>
    public int MaxAttempts
    {
        get => _maxAttempts;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"invalid value '{value}' for '{nameof(MaxAttempts)}', it must be greater than 0");
            }
            _maxAttempts = value;
        }
    }

    /// <summary>Gets or sets the maximum size of the request payload in bytes. Requests with a larger payload or
    /// a payload continuation are not retryable.</summary>
    /// <value>The maximum payload size in bytes. The default value is 1 MB.</value>
    public int MaxPayloadSize
    {
        get => _maxPayloadSize;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"invalid value '{value}' for '{nameof(MaxPayloadSize)}' it must be greater than 0");
            }
            _maxPayloadSize = value;
        }
    }

    private int _maxAttempts = 2;
    private int _maxPayloadSize = 1024 * 1024;
}
