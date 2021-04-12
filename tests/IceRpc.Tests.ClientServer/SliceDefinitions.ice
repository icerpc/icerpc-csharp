// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

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

    interface StressTestService
    {
        void opSendByteSeq(IceRpc::ByteSeq data);
        IceRpc::ByteSeq opReceiveByteSeq(int size);
    }
}
