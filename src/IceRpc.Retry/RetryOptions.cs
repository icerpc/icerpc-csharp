// Copyright (c) ZeroC, Inc.

namespace IceRpc.Retry;

/// <summary>A property bag used to configure a <see cref="RetryInterceptor" />.</summary>
public sealed record class RetryOptions
{
    /// <summary>Gets or sets the maximum number of attempts for retrying a request.</summary>
    /// <value>The maximum number of attempts for retrying a request. Defaults to <c>2</c> attempts.</value>
    public int MaxAttempts
    {
        get => _maxAttempts;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Invalid value '{value}' for '{nameof(MaxAttempts)}', it must be greater than 0.");
            }
            _maxAttempts = value;
        }
    }

    /// <summary>Gets or sets the maximum size of the request payload in bytes for which retries would be considered.
    /// Requests with a larger payload or a payload continuation are never retried.</summary>
    /// <value>The maximum request payload size in bytes for which retries will be attempted. Defaults to <c>1</c> MB.
    /// </value>
    /// <remarks>The ability to retry depends on keeping the request payload around until a successful response has been
    /// received or retries are no longer possible, this setting affects the working memory that the application will
    /// consume.</remarks>
    public int MaxPayloadSize
    {
        get => _maxPayloadSize;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Invalid value '{value}' for '{nameof(MaxPayloadSize)}' it must be greater than 0.");
            }
            _maxPayloadSize = value;
        }
    }

    private int _maxAttempts = 2;
    private int _maxPayloadSize = 1024 * 1024;
}
