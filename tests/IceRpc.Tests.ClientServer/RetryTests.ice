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
        void op(bool kill);
        idempotent int opIdempotent(int c);
        void opNotIdempotent();
        void opSystemException();
        int opAfterDelay(int retries, int delay);
        void opBidirRetry(RetryBidirService prx);
        void opWithData(int retries, int delay, RetryByteSeq data);
    }

    interface RetryReplicatedService
    {
        void otherReplica();
    }
}
