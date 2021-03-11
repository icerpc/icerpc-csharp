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
        idempotent void opIdempotent(int retries, int delay, bool kill);
        void opNotIdempotent(int retries, int delay, bool kill);
        void opBidirRetry(RetryBidirService prx);
        void opWithData(int retries, int delay, RetryByteSeq data);
    }

    interface RetryReplicatedService
    {
        void otherReplica();
    }
}
