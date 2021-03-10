// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    interface UdpPingReply
    {
        void reply();
    }

    sequence<byte> UdpByteSeq;

    interface UdpService
    {
        void ping(UdpPingReply reply);
        void sendByteSeq(UdpByteSeq seq, UdpPingReply? reply);
        void pingBiDir(string path);
    }
}
