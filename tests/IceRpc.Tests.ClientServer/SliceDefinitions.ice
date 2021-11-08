// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    interface Greeter
    {
        void sayHello();
    }

    interface StressTest
    {
        void opSendByteSeq(sequence<byte> data);
        sequence<byte> opReceiveByteSeq(int size);
    }
}
