// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc::Tests::ClientServer
{
    enum CloseMode
    {
        Forcefully,
        Gracefully
    }

    interface ConnectionTest
    {
        void close(CloseMode mode);
        void enter();
        void finishDispatch();
        void initiatePing();
        void opWithPayload(IceRpc::ByteSeq seq);
        void release();
        void sleep(int ms);
        void startDispatch();
    }
}
