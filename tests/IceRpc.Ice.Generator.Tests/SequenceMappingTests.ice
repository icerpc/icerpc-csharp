// Copyright (c) ZeroC, Inc.

#include "EnumTests.ice"
#include "StructTests.ice"

module IceRpc::Ice::Generator::Tests
{
    sequence<int> IntSeq;
    ["cs:generic:IceRpc.Ice.Generator.Tests.CustomSequence"] sequence<int> CustomIntSeq;
    ["cs:generic:System.Collections.Generic.HashSet"] sequence<int> HashIntSet;

    sequence<string> StringSeq;
    ["cs:generic:IceRpc.Ice.Generator.Tests.CustomSequence"] sequence<string> CustomStringSeq;

    sequence<MyEnum> MyEnumSeq;
    ["cs:generic:IceRpc.Ice.Generator.Tests.CustomSequence"] sequence<MyEnum> CustomMyEnumSeq;

    sequence<MyStruct> MyStructSeq;
    sequence<MyStructSeq> MyStructSeqSeq;
    sequence<MyStructSeqSeq> MyStructSeqSeqSeq;

    sequence<byte> ByteSeq;
    sequence<ByteSeq> ByteSeqSeq;
    sequence<ByteSeqSeq> ByteSeqSeqSeq;

    interface SequenceMappingOperations
    {
        IntSeq returnSequenceOfInt();
        void sendSequenceOfInt(IntSeq p);

        StringSeq returnSequenceOfString();
        void sendSequenceOfString(StringSeq p);

        MyEnumSeq returnSequenceOfMyEnum();
        void sendSequenceOfMyEnum(MyEnumSeq p);

        MyStructSeq returnSequenceOfMyStruct();
        void sendSequenceOfMyStruct(MyStructSeq p);

        CustomIntSeq returnCustomSequenceOfInt();
        void sendCustomSequenceOfInt(CustomIntSeq p);

        CustomStringSeq returnCustomSequenceOfString();
        void sendCustomSequenceOfString(CustomStringSeq p);

        CustomMyEnumSeq returnCustomSequenceOfMyEnum();
        void sendCustomSequenceOfMyEnum(CustomMyEnumSeq p);

        HashIntSet returnHashSetOfInt();
        void sendHashSetOfInt(HashIntSet p);

        ByteSeqSeqSeq opNumericTypeNestedSequence(ByteSeqSeqSeq p1);

        MyStructSeqSeqSeq opStructNestedSequence(MyStructSeqSeqSeq p1);

        CustomIntSeq opReturnAndOut(out CustomIntSeq r1);
    }
}
