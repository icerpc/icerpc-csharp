[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>
#include <StructTests.ice>
#include <SequenceTests.ice>
#include <ClassTests.ice>
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

    class TaggedWithCustom
    {
        tag(1) MyStructList? mMyStructList;
        tag(2) AnotherStructList? mAnotherStructList;
    }

    interface ClassTag
    {
        AnyClass pingPong(AnyClass o);
    }
}
