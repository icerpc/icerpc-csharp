// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Retry;

namespace IceRpc.Configure;

/// <summary>Options class to configure <see cref="RetryInterceptor"/>.</summary>
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
                    $"Invalid value '{value}' for '{nameof(MaxAttempts)}', it must be greater than 0.");
            }
            _maxAttempts = value;
        }
    }

    /// <summary>Gets or sets the maximum payload size in bytes for a request to be retryable. Requests with a
    /// bigger payload size are released after sent and cannot be retried.</summary>
    /// <value>The maximum payload size in bytes. The default value is 1 MB.</value>
    public int RequestMaxSize
    {
        get => _requestMaxSize;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    $"Invalid value '{value}' for '{nameof(RequestMaxSize)}' it must be greater than 0.");
            }
            _requestMaxSize = value;
        }
    }

    private int _maxAttempts = 2;
    private int _requestMaxSize = 1024 * 1024;
}
