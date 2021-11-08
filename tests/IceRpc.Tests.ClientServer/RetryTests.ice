// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    exception RetrySystemFailure
    {
    }

    interface RetryBidirTest
    {
        void otherReplica();
        void afterDelay(int n);
    }

    interface RetryTest
    {
        idempotent void opIdempotent(int failedAttempts, bool killConnection);
        void opNotIdempotent(int failedAttempts, bool killConnection);
        void opWithData(int failedAttempts, int delay, sequence<byte> data); // TODO rename this parameter to 'retryData'.
        void opRetryAfterDelay(int failedAttempts, int delay);
        void opRetryNo();
    }

    interface RetryReplicatedTest
    {
        void otherReplica();
    }
}
