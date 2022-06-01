// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Globalization;

namespace IceRpc
{
    /// <summary>The retry policy can be specified when constructing a <see cref="RemoteException"/>. With the icerpc
    /// protocol, the retry policy is transmitted as a field with the response and later decoded by the client's.</summary>
    public sealed record class RetryPolicy
    {
        /// <summary>Gets the Immediately policy instance, the Immediately policy specifies that the exception can be
        /// retried without any delay.</summary>
        public static RetryPolicy Immediately { get; } = new(Retryable.AfterDelay, TimeSpan.Zero);

        /// <summary>Gets the NoRetry policy instance, the NoRetry policy specifies that the exception cannot be
        /// retried. This is the default policy when no policy is specified.</summary>
        public static RetryPolicy NoRetry { get; } = new(Retryable.No);

        /// <summary>Gets the OtherReplica policy instance, the OtherReplica policy specifies that the exception can
        /// be retried on a different replica.</summary>
        public static RetryPolicy OtherReplica { get; } = new(Retryable.OtherReplica);

        /// <summary>Gets the retry policy ability for retrying.</summary>
        public Retryable Retryable { get; }

        /// <summary>Gets the retry policy delay to apply for retries.</summary>
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

        /// <summary>Encodes this retry policy instance into the given encoder.</summary>
        /// <param name="decoder">The decoder.</param>
        public RetryPolicy(ref SliceDecoder decoder)
        {
            Retryable = decoder.DecodeRetryable();
            Delay = Retryable == Retryable.AfterDelay ?
                TimeSpan.FromMilliseconds(decoder.DecodeVarUInt62()) : TimeSpan.Zero;
        }

        /// <summary>Encodes this retry policy instance into the given encoder.</summary>
        /// <param name="encoder">The encoder.</param>
        public void Encode(ref SliceEncoder encoder)
        {
            encoder.EncodeRetryable(Retryable);
            if (Retryable == Retryable.AfterDelay)
            {
                encoder.EncodeVarUInt32((uint)Delay.TotalMilliseconds);
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
