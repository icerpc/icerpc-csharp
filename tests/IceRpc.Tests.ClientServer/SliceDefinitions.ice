// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc::Tests::ClientServer
{
    interface Greeter
    {
        void sayHello();
    }

    interface StressTest
    {
        void opSendByteSeq(IceRpc::ByteSeq data);
        IceRpc::ByteSeq opReceiveByteSeq(int size);
    }
}
