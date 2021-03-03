// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
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
        void sleep(int seconds);
        void initiatePing();
    }
}
