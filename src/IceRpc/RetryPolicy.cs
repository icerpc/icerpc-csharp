// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Globalization;

namespace IceRpc
{
    /// <summary>The retry policy can be specified when constructing a <see cref="RemoteException"/>. It's also used
    /// as a request feature to retry (or not retry) when a local exception is thrown during an invocation.</summary>
    public sealed record class RetryPolicy
    {
        /// <summary>The Immediately policy specifies that the exception can be retried without any delay.</summary>
        public static RetryPolicy Immediately { get; } = new(Retryable.AfterDelay, TimeSpan.Zero);

        /// <summary>The NoRetry policy specifies that the exception cannot be retried. This is the default policy
        /// when no policy is specified.</summary>
        public static RetryPolicy NoRetry { get; } = new(Retryable.No);

        /// <summary>The OtherReplica policy specifies that the exception can be retried on a different replica.
        /// </summary>
        public static RetryPolicy OtherReplica { get; } = new(Retryable.OtherReplica);

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
            Retryable.AfterDelay => $"after {DelayToString(Delay)} delay",
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

        internal void Encode(IceEncoder encoder)
        {
            encoder.EncodeRetryable(Retryable);
            if (Retryable == Retryable.AfterDelay)
            {
                encoder.EncodeVarUInt((uint)Delay.TotalMilliseconds);
            }
        }

        private static string DelayToString(TimeSpan ts)
        {
            FormattableString message;
            if (ts == TimeSpan.Zero)
            {
                return "0ms";
            }
            else if (ts == Timeout.InfiniteTimeSpan)
            {
                return "infinite";
            }
            else if (ts.Milliseconds != 0)
            {
                message = $"{ts.TotalMilliseconds}ms";
            }
            else if (ts.Seconds != 0)
            {
                message = $"{ts.TotalSeconds}s";
            }
            else if (ts.Minutes != 0)
            {
                message = $"{ts.TotalMinutes}m";
            }
            else if (ts.Hours != 0)
            {
                message = $"{ts.TotalHours}h";
            }
            else if (ts.Days != 0)
            {
                message = $"{ts.TotalDays}d";
            }
            else
            {
                message = $"{ts.TotalMilliseconds}ms";
            }

            return message.ToString(CultureInfo.InvariantCulture);
        }

        private RetryPolicy(Retryable retryable, TimeSpan delay = default)
        {
            Retryable = retryable;
            Delay = delay;
        }
    }
}
