using System.Collections.Immutable;

namespace IceRpc.Features
{
    /// <summary>A feature that represents a <see cref="IceRpc.RetryPolicy"/>.</summary>
    public sealed record class RetryPolicyFeature
    {
        /// <summary>The Immediately policy specifies that the exception can be retried without any delay.</summary>
        public static RetryPolicyFeature Immediately { get; } =
            new RetryPolicyFeature { Value = RetryPolicy.Immediately };

        /// <summary>The NoRetry policy specifies that the exception cannot be retried. This is the default policy
        /// when no policy is specified.</summary>
        public static RetryPolicyFeature NoRetry { get; } =
            new RetryPolicyFeature { Value = RetryPolicy.NoRetry };

        /// <summary>The OtherReplica policy specifies that the exception can be retried on a different replica.
        /// </summary>
        public static RetryPolicyFeature OtherReplica { get; } =
            new RetryPolicyFeature { Value = RetryPolicy.OtherReplica };

        /// <summary>Gets or creates a retry policy feature from a retry policy.</summary>
        /// <param name="retryPolicy">The retry policy.</param>
        /// <returns>The retry policy features.</returns>
        public static RetryPolicyFeature FromRetryPolicy(RetryPolicy retryPolicy) => retryPolicy.Retryable switch
        {
            Retryable.No => NoRetry,
            Retryable.AfterDelay =>
                retryPolicy.Delay == TimeSpan.Zero ? Immediately : new RetryPolicyFeature { Value = retryPolicy },
            Retryable.OtherReplica => OtherReplica,
            _ => throw new ArgumentException($"invalid Retryable value '{retryPolicy.Retryable}'", nameof(retryPolicy)),
        };

        /// <summary>The associated RetryPolicy.</summary>
        public RetryPolicy Value { get; init; } = RetryPolicy.NoRetry;
    }
}
