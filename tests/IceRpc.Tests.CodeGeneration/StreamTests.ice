// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::CodeGeneration::Stream
{
    interface Streams
    {
        stream byte opStreamByteReceive0();
        (byte r1, stream byte r2) opStreamByteReceive1();
        (byte r1, int r2, stream byte r3) opStreamByteReceive2();

        void opStreamByteSend0(stream byte p1);
        void opStreamByteSend1(byte p1, stream byte p2);
        void opStreamByteSend2(byte p1, int p2, stream byte p3);

        stream byte opStreamByteSendReceive0(stream byte p1);
        (byte r1, stream byte r2) opStreamByteSendReceive1(byte p1, stream byte p2);
        (byte r1, int r2, stream byte r3) opStreamByteSendReceive2(byte p1, int p2, stream byte p3);
    }
}
