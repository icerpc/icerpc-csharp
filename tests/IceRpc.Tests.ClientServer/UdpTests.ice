// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <Ice/Identity.ice>

module IceRpc::Tests::ClientServer
{
    interface UdpPingReply
    {
        void reply();
    }

    sequence<byte> UdpByteSeq;

    interface UdpService
    {
        int getValue();
        void ping(UdpPingReply reply);
        void sendByteSeq(UdpByteSeq seq, UdpPingReply? reply);
        void pingBiDir(Ice::Identity id);
        void shutdown();
    }
}
