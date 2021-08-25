// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Diagnostics;
using System.Text;

namespace IceRpc
{
    /// <summary>The retry policy that can be specified when constructing a remote exception.</summary>
    public readonly struct RetryPolicy : IEquatable<RetryPolicy>
    {
        /// <summary>The retry policy ability for retrying.</summary>
        public readonly Retryable Retryable;
        /// <summary>The retry policy delay to apply for retries.</summary>
        public readonly TimeSpan Delay;

        /// <summary>The NoRetry policy specifies that the exception cannot be retried. This is the default policy
        /// when no policy is specified.</summary>
        public static readonly RetryPolicy NoRetry = new(Retryable.No);

        /// <summary>The OtherReplica policy specifies that the exception can be retried on a different replica.
        /// </summary>
        public static readonly RetryPolicy OtherReplica = new(Retryable.OtherReplica);

        /// <summary>The Immediately policy specifies that the exception can be retried without any delay.</summary>
        public static readonly RetryPolicy Immediately = new(Retryable.AfterDelay, TimeSpan.Zero);

        /// <summary>Creates a retry policy that specifies that the exception can be retried after the given delay.
        /// </summary>
        /// <param name="delay">The delay after which the exception can be retried.</param>
        /// <returns>The retry policy.</returns>
        public static RetryPolicy AfterDelay(TimeSpan delay) => new(Retryable.AfterDelay, delay);

        /// <inheritdoc/>
        public bool Equals(RetryPolicy other) => Retryable == other.Retryable && Delay == other.Delay;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is RetryPolicy other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Retryable, Delay);

        /// <inheritdoc/>
        public override string? ToString() => Retryable switch
        {
            Retryable.AfterDelay => $"after {Delay.ToPropertyValue()} delay",
            Retryable.OtherReplica => "other replica",
            Retryable.No => "no retry",
            _ => "unknown"
        };

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(RetryPolicy lhs, RetryPolicy rhs) => lhs.Equals(rhs);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(RetryPolicy lhs, RetryPolicy rhs) => !(lhs == rhs);

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
