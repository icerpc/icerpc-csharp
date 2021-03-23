// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    // TODO: eliminate Service suffix?

    interface GreeterTestService
    {
        void sayHello();
    }

    interface LoggingTestService
    {
        void op();
    }

    sequence<byte> StressByteSeq;

    interface StressTestService
    {
        void opSendByteSeq(StressByteSeq data);
        StressByteSeq opReceiveByteSeq(int size);
    }

    interface ConnectionTestService
    {
        void enter();
        void release();
        void initiatePing();
    }
}
