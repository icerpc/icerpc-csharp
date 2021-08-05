// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <StructTests.ice>

module IceRpc::Tests::CodeGeneration
{
    interface StreamParamOperations
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

        stream MyStruct opStreamMyStructReceive0();
        (MyStruct r1, stream MyStruct r2) opStreamMyStructReceive1();
        (MyStruct r1, MyStruct r2, stream MyStruct r3) opStreamMyStructReceive2();

        void opStreamMyStructSend0(stream MyStruct p1);
        void opStreamMyStructSend1(MyStruct p1, stream MyStruct p2);
        void opStreamMyStructSend2(MyStruct p1, MyStruct p2, stream MyStruct p3);

        stream MyStruct opStreamMyStructSendReceive0(stream MyStruct p1);
        (MyStruct r1, stream MyStruct r2) opStreamMyStructSendReceive1(MyStruct p1, stream MyStruct p2);
        (MyStruct r1, MyStruct r2, stream MyStruct r3) opStreamMyStructSendReceive2(
            MyStruct p1,
            MyStruct p2,
            stream MyStruct p3);

        stream AnotherStruct opStreamAnotherStructReceive0();
        (AnotherStruct r1, stream AnotherStruct r2) opStreamAnotherStructReceive1();
        (AnotherStruct r1, AnotherStruct r2, stream AnotherStruct r3) opStreamAnotherStructReceive2();

        void opStreamAnotherStructSend0(stream AnotherStruct p1);
        void opStreamAnotherStructSend1(AnotherStruct p1, stream AnotherStruct p2);
        void opStreamAnotherStructSend2(AnotherStruct p1, AnotherStruct p2, stream AnotherStruct p3);

        stream AnotherStruct opStreamAnotherStructSendReceive0(stream AnotherStruct p1);
        (AnotherStruct r1, stream AnotherStruct r2) opStreamAnotherStructSendReceive1(
            AnotherStruct p1,
            stream AnotherStruct p2);
        (AnotherStruct r1, AnotherStruct r2, stream AnotherStruct r3) opStreamAnotherStructSendReceive2(
            AnotherStruct p1,
            AnotherStruct p2,
            stream AnotherStruct p3);
    }
}
