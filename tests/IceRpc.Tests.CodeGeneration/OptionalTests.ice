// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>
#include <StructTests.ice>
#include <SequenceTests.ice>
#include <DictionaryTests.ice>

module IceRpc::Tests::CodeGeneration
{
    struct OneOptional
    {
        int? a;
    }

    struct MultiOptional
    {
        byte? mByte;
        bool? mBool;
        short? mShort;
        int? mInt;
        long? mLong;
        float? mFloat;
        double? mDouble;
        ushort? mUShort;
        uint? mUInt;
        ulong? mULong;
        varint? mVarInt;
        varlong? mVarLong;
        varuint? mVarUInt;
        varulong? mVarULong;
        string? mString;

        MyEnum? mMyEnum;
        MyStruct? mMyStruct;
        AnotherStruct? mAnotherStruct;

        ByteSeq? mByteSeq;
        StringSeq? mStringSeq;
        ShortSeq? mShortSeq;
        MyEnumSeq? mMyEnumSeq;
        MyStructSeq? mMyStructSeq;
        AnotherStructSeq? mAnotherStructSeq;

        IntDict? mIntDict;
        StringDict? mStringDict;
        UShortSeq? mUShortSeq;
        VarULongSeq? mVarULongSeq;
        VarIntSeq? mVarIntSeq;

        ByteDict? mByteDict;
        MyStructDict? mMyStructDict;
        AnotherStructDict? mAnotherStructDict;
    }

    interface OptionalOperations
    {
        OneOptional? pingPongOne(OneOptional? o);
        MultiOptional? pingPongMulti(MultiOptional? o);

        (byte? r1, byte? r2) opByte(byte? p1);

        (bool? r1, bool? r2) opBool(bool? p1);

        (short? r1, short? r2) opShort(short? p1);

        (int? r1, int? r2) opInt(int? p1);

        (long? r1, long? r2) opLong(long? p1);

        (float? r1, float? r2) opFloat(float? p1);

        (double? r1, double? r2) opDouble(double? p1);

        (string? r1, string? r2) opString(string? p1);

        (MyEnum? r1, MyEnum? r2) opMyEnum(MyEnum? p1);

        (MyStruct? r1, MyStruct? r2) opMyStruct(MyStruct? p1);

        (AnotherStruct? r1, AnotherStruct? r2) opAnotherStruct(AnotherStruct? p1);

        (ByteSeq? r1, ByteSeq? r2) opByteSeq(ByteSeq? p1);
        (ByteList? r1, ByteList? r2) opByteList(ByteList? p1);

        (BoolSeq? r1, BoolSeq? r2) opBoolSeq(BoolSeq? p1);
        (BoolList? r1, BoolList? r2) opBoolList(BoolList? p1);

        (ShortSeq? r1, ShortSeq? r2) opShortSeq(ShortSeq? p1);
        (ShortList? r1, ShortList? r2) opShortList(ShortList? p1);

        (IntSeq? r1, IntSeq? r2) opIntSeq(IntSeq? p1);
        (IntList? r1, IntList? r2) opIntList(IntList? p1);

        (LongSeq? r1, LongSeq? r2) opLongSeq(LongSeq? p1);
        (LongList? r1, LongList? r2) opLongList(LongList? p1);

        (FloatSeq? r1, FloatSeq? r2) opFloatSeq(FloatSeq? p1);
        (FloatList? r1, FloatList? r2) opFloatList(FloatList? p1);

        (DoubleSeq? r1, DoubleSeq? r2) opDoubleSeq(DoubleSeq? p1);
        (DoubleList? r1, DoubleList? r2) opDoubleList(DoubleList? p1);

        (StringSeq? r1, StringSeq? r2) opStringSeq(StringSeq? p1);
        (StringList? r1, StringList? r2) opStringList(StringList? p1);

        (MyStructSeq? r1, MyStructSeq? r2) opMyStructSeq(MyStructSeq? p1);
        (MyStructList? r1, MyStructList? r2) opMyStructList(MyStructList? p1);

        (AnotherStructSeq? r1, AnotherStructSeq? r2) opAnotherStructSeq(AnotherStructSeq? p1);
        (AnotherStructList? r1, AnotherStructList? r2) opAnotherStructList(AnotherStructList? p1);

        (IntDict? r1, IntDict? r2) opIntDict(IntDict? p1);
        (StringDict? r1, StringDict? r2) opStringDict(StringDict? p1);

        [marshaled-result] MyStruct? opMyStructMarshaledResult(MyStruct? p1);
        [marshaled-result] StringSeq? opStringSeqMarshaledResult(StringSeq? p1);
        [marshaled-result] IntDict? opIntDictMarshaledResult(IntDict? p1);
    }
}
