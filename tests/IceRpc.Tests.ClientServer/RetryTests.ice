// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    exception RetrySystemFailure
    {
    }

    sequence<byte> RetryByteSeq;

    interface RetryBidirService
    {
        void otherReplica();
        void afterDelay(int n);
    }

    interface RetryService
    {
        idempotent void opIdempotent(int failedAttempts, bool killConnection);
        void opNotIdempotent(int failedAttempts, bool killConnection);
        void opWithData(int failedAttempts, int delay, RetryByteSeq data);
        void opRetryAfterDelay(int failedAttempts, int delay);
        void opRetryNo();
    }

    interface RetryReplicatedService
    {
        void otherReplica();
    }
}
