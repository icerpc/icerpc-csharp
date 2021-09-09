// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;

namespace IceRpc
{
    /// <summary>The retry policy that can be specified when constructing a <see cref="RemoteException"/>.</summary>
    public sealed record class RetryPolicy
    {
        /// <summary>The Immediately policy specifies that the exception can be retried without any delay.</summary>
        public static readonly RetryPolicy Immediately = new(Retryable.AfterDelay, TimeSpan.Zero);

        /// <summary>The NoRetry policy specifies that the exception cannot be retried. This is the default policy
        /// when no policy is specified.</summary>
        public static readonly RetryPolicy NoRetry = new(Retryable.No);

        /// <summary>The OtherReplica policy specifies that the exception can be retried on a different replica.
        /// </summary>
        public static readonly RetryPolicy OtherReplica = new(Retryable.OtherReplica);

        /// <summary>The retry policy ability for retrying.</summary>
        public Retryable Retryable { get; }

        /// <summary>The retry policy delay to apply for retries.</summary>
        public TimeSpan Delay { get; }

        /// <summary>Creates a retry policy that specifies that the exception can be retried after the given delay.
        /// </summary>
        /// <param name="delay">The delay after which the exception can be retried.</param>
        /// <returns>The retry policy.</returns>
        public static RetryPolicy AfterDelay(TimeSpan delay) => new(Retryable.AfterDelay, delay);

        /// <inheritdoc/>
        public override string ToString() => Retryable switch
        {
            Retryable.AfterDelay => $"after {Delay.ToPropertyValue()} delay",
            Retryable.OtherReplica => "other replica",
            Retryable.No => "no retry",
            _ => "unknown"
        };

        internal RetryPolicy(IceDecoder decoder)
        {
            Retryable = decoder.DecodeRetryable();
            Delay = Retryable == Retryable.AfterDelay ?
                TimeSpan.FromMilliseconds(decoder.DecodeVarULong()) : TimeSpan.Zero;
        }

        private RetryPolicy(Retryable retryable, TimeSpan delay = default)
        {
            Retryable = retryable;
            Delay = delay;
        }
    }
}
