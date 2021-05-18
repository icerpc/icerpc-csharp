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
    class OneTagged
    {
        tag(1) int? a;
    }

    class MultiTagged
    {
        tag(1) byte? mByte;
        tag(2) bool? mBool;
        tag(3) short? mShort;
        tag(4) int? mInt;
        tag(5) long? mLong;
        tag(6) float? mFloat;
        tag(7) double? mDouble;
        tag(8) ushort? mUShort;
        tag(9) uint? mUInt;
        tag(10) ulong? mULong;
        tag(11) varint? mVarInt;
        tag(12) varlong? mVarLong;
        tag(13) varuint? mVarUInt;
        tag(14) varulong? mVarULong;
        tag(15) string? mString;

        tag(20) MyEnum? mMyEnum;
        tag(21) MyStruct? mMyStruct;
        tag(22) AnotherStruct? mAnotherStruct;

        tag(30) ByteSeq? mByteSeq;
        tag(31) StringSeq? mStringSeq;
        tag(32) ShortSeq? mShortSeq;
        tag(33) MyEnumSeq? mMyEnumSeq;
        tag(34) MyStructSeq? mMyStructSeq;
        tag(35) AnotherStructSeq? mAnotherStructSeq;

        tag(40) IntDict? mIntDict;
        tag(41) StringDict? mStringDict;
        tag(42) UShortSeq? mUShortSeq;
        tag(43) VarULongSeq? mVarULongSeq;
        tag(44) VarIntSeq? mVarIntSeq;

        tag(50) ByteDict? mByteDict;
        tag(51) MyStructDict? mMyStructDict;
        tag(52) AnotherStructDict? mAnotherStructDict;
    }

    class A
    {
        int mInt1;
        tag(1) int? mInt2;
        tag(50) int? mInt3;
        tag(500) int? mInt4;
    }

    [preserve-slice]
    class B : A
    {
        int mInt5;
        tag(10) int? mInt6;
    }

    class C : B
    {
        string mString1;
        tag(890) string? mString2;
    }

    class WD
    {
        tag(1) int? mInt;
        tag(2) string? mString;
    }

    exception TaggedException
    {
        bool mBool;
        tag(1) int? mInt;
        tag(2) string? mString;
        tag(50) AnotherStruct? mAnotherStruct;
    }

    exception DerivedException : TaggedException
    {
        tag(600) string? mString1;
        tag(601) AnotherStruct? mAnotherStruct1;
    }

    exception RequiredException : TaggedException
    {
        string mString1;
        AnotherStruct mAnotherStruct1;
    }

    class TaggedWithCustom
    {
        tag(1) MyStructList? mMyStructList;
        tag(2) AnotherStructList? mAnotherStructList;
    }

    interface TaggedOperations
    {
        AnyClass pingPong(AnyClass o);

        void opTaggedException(tag(1) int? p1, tag(2) string? p2, tag(3) AnotherStruct? p3);

        void opDerivedException(tag(1) int? p1, tag(2) string? p2, tag(3) AnotherStruct? p3);

        void opRequiredException(tag(1) int? p1, tag(2) string? p2, tag(3) AnotherStruct? p3);

        (tag(1) byte? r1, tag(2) byte? r2) opByte(tag(1) byte? p1);

        (tag(1) bool? r1, tag(2) bool? r2) opBool(tag(1) bool? p1);

        (tag(1) short? r1, tag(2) short? r2) opShort(tag(1) short? p1);

        (tag(1) int? r1, tag(2) int? r2) opInt(tag(1) int? p1);

        (tag(1) long? r1, tag(2) long? r2) opLong(tag(1) long? p1);

        (tag(1) float? r1, tag(2) float? r2) opFloat(tag(1) float? p1);

        (tag(1) double? r1, tag(2) double? r2) opDouble(tag(1) double? p1);

        (tag(1) string? r1, tag(2) string? r2) opString(tag(1) string? p1);

        (tag(1) MyEnum? r1, tag(2) MyEnum? r2) opMyEnum(tag(1) MyEnum? p1);

        (tag(1) MyStruct? r1, tag(2) MyStruct? r2) opMyStruct(tag(1) MyStruct? p1);

        (tag(1) AnotherStruct? r1, tag(2) AnotherStruct? r2) opAnotherStruct(tag(1) AnotherStruct? p1);

        (tag(1) ByteSeq? r1, tag(2) ByteSeq? r2) opByteSeq(tag(1) ByteSeq? p1);
        (tag(1) ByteList? r1, tag(2) ByteList? r2) opByteList(tag(1) ByteList? p1);

        (tag(1) BoolSeq? r1, tag(2) BoolSeq? r2) opBoolSeq(tag(1) BoolSeq? p1);
        (tag(1) BoolList? r1, tag(2) BoolList? r2) opBoolList(tag(1) BoolList? p1);

        (tag(1) ShortSeq? r1, tag(2) ShortSeq? r2) opShortSeq(tag(1) ShortSeq? p1);
        (tag(1) ShortList? r1, tag(2) ShortList? r2) opShortList(tag(1) ShortList? p1);

        (tag(1) IntSeq? r1, tag(2) IntSeq? r2) opIntSeq(tag(1) IntSeq? p1);
        (tag(1) IntList? r1, tag(2) IntList? r2) opIntList(tag(1) IntList? p1);

        (tag(1) LongSeq? r1, tag(2) LongSeq? r2) opLongSeq(tag(1) LongSeq? p1);
        (tag(1) LongList? r1, tag(2) LongList? r2) opLongList(tag(1) LongList? p1);

        (tag(1) FloatSeq? r1, tag(2) FloatSeq? r2) opFloatSeq(tag(1) FloatSeq? p1);
        (tag(1) FloatList? r1, tag(2) FloatList? r2) opFloatList(tag(1) FloatList? p1);

        (tag(1) DoubleSeq? r1, tag(2) DoubleSeq? r2) opDoubleSeq(tag(1) DoubleSeq? p1);
        (tag(1) DoubleList? r1, tag(2) DoubleList? r2) opDoubleList(tag(1) DoubleList? p1);

        (tag(1) StringSeq? r1, tag(2) StringSeq? r2) opStringSeq(tag(1) StringSeq? p1);
        (tag(1) StringList? r1, tag(2) StringList? r2) opStringList(tag(1) StringList? p1);

        (tag(1) MyStructSeq? r1, tag(2) MyStructSeq? r2) opMyStructSeq(tag(1) MyStructSeq? p1);
        (tag(1) MyStructList? r1, tag(2) MyStructList? r2) opMyStructList(tag(1) MyStructList? p1);

        (tag(1) AnotherStructSeq? r1, tag(2) AnotherStructSeq? r2) opAnotherStructSeq(tag(1) AnotherStructSeq? p1);
        (tag(1) AnotherStructList? r1, tag(2) AnotherStructList? r2) opAnotherStructList(tag(1) AnotherStructList? p1);

        (tag(1) IntDict? r1, tag(2) IntDict? r2) opIntDict(tag(1) IntDict? p1);
        (tag(1) StringDict? r1, tag(2) StringDict? r2) opStringDict(tag(1) StringDict? p1);

        void opVoid();

        [marshaled-result] tag(1) AnotherStruct? opAnotherStructMarshaledResult(tag(1) AnotherStruct? p1);
        [marshaled-result] tag(1) StringSeq? opStringSeqMarshaledResult(tag(1) StringSeq? p1);
        [marshaled-result] tag(1) IntDict? opIntDictMarshaledResult(tag(1) IntDict? p1);
    }
}
