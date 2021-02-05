// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    sequence<byte> StressByteSeq;

    interface StressTestService
    {
        void opSendByteSeq(StressByteSeq data);
        StressByteSeq opReceiveByteSeq(int size);
    }
}
