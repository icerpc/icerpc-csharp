//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Test::Enums
{

enum ByteEnum
{
    benum1,
    benum2,
    benum3 = 10,
    benum4,
    benum5 = 20,
    benum6,
    benum7 = 30,
    benum8,
    benum9 = 40,
    benum10,
    benum11 = 226
}
sequence<ByteEnum> ByteEnumSeq;

enum ShortEnum
{
    senum1 = 3,
    senum2,
    senum3 = 10,
    senum4,
    senum5 = 20,
    senum6,
    senum7 = 30,
    senum8,
    senum9 = 40,
    senum10,
    senum11 = 32766
}
sequence<ShortEnum> ShortEnumSeq;

enum IntEnum
{
    ienum1,
    ienum2,
    ienum3 = 10,
    ienum4,
    ienum5 = 20,
    ienum6,
    ienum7 = 30,
    ienum8,
    ienum9 = 40,
    ienum10,
    ienum11 = 2147483647,
    ienum12 = 2147483646
}
sequence<IntEnum> IntEnumSeq;

enum SimpleEnum
{
    red,
    green,
    blue
}
sequence<SimpleEnum> SimpleEnumSeq;

enum FLByteEnum : byte // Fixed-length
{
    benum1,
    benum2,
    benum3 = 10,
    benum4,
    benum5 = 20,
    benum6,
    benum7 = 30,
    benum8,
    benum9 = 40,
    benum10,
    benum11 = 226
}
sequence<FLByteEnum> FLByteEnumSeq;

enum FLShortEnum : short
{
    senum1 = -3,
    senum2,
    senum3 = 10,
    senum4,
    senum5 = 20,
    senum6,
    senum7 = 30,
    senum8,
    senum9 = 40,
    senum10,
    senum11 = 32766
}
sequence<FLShortEnum> FLShortEnumSeq;

enum FLUShortEnum : ushort
{
    senum1 = 3,
    senum2,
    senum3 = 10,
    senum4,
    senum5 = 20,
    senum6,
    senum7 = 30,
    senum8,
    senum9 = 40,
    senum10,
    senum11 = 32766
}
sequence<FLUShortEnum> FLUShortEnumSeq;

enum FLIntEnum : int
{
    ienum1 = -3,
    ienum2,
    ienum3 = 10,
    ienum4,
    ienum5 = 20,
    ienum6,
    ienum7 = 30,
    ienum8,
    ienum9 = 40,
    ienum10,
    ienum11 = 2147483647,
    ienum12 = 2147483646
}
sequence<FLIntEnum> FLIntEnumSeq;

enum FLUIntEnum : uint
{
    ienum1,
    ienum2,
    ienum3 = 10,
    ienum4,
    ienum5 = 20,
    ienum6,
    ienum7 = 30,
    ienum8,
    ienum9 = 40,
    ienum10,
    ienum11 = 2147483647,
    ienum12 = 2147483646
}
sequence<FLUIntEnum> FLUIntEnumSeq;

enum FLSimpleEnum : byte
{
    red,
    green,
    blue
}
sequence<FLSimpleEnum> FLSimpleEnumSeq;

[cs:attribute(System.Flags)] unchecked enum MyFlags : uint
{
    E0 = 1,
    E1 = 2,
    E2 = 4,
    E3 = 8,
    E4 = 16,
    E5 = 32,
    E6 = 64,
    E7 = 128,
    E8 = 256,
    E9 = 512,
    E10 = 1024,
    E11 = 2048,
    E12 = 4096,
    E13 = 8192,
    E14 = 0x04000,
    E15 = 0x08000,
    E16 = 0x10000,
    E17 = 0x20000,
    E18 = 0x40000,
    E19 = 0x80000,
    // don't need them all for this test
    E29 = 0x20000000,
    E30 = 0x40000000,
    E31 = 0x80000000
}

sequence<MyFlags> MyFlagsSeq;

interface TestIntf
{
    (ByteEnum r1, ByteEnum r2) opByte(ByteEnum b1);
    (ShortEnum r1, ShortEnum r2) opShort(ShortEnum s1);
    (IntEnum r1, IntEnum r2) opInt(IntEnum i1);
    (SimpleEnum r1, SimpleEnum r2) opSimple(SimpleEnum s1);

    (ByteEnumSeq r1, ByteEnumSeq r2) opByteSeq(ByteEnumSeq b1);
    (ShortEnumSeq r1, ShortEnumSeq r2) opShortSeq(ShortEnumSeq s1);
    (IntEnumSeq r1, IntEnumSeq r2) opIntSeq(IntEnumSeq i1);
    (SimpleEnumSeq r1, SimpleEnumSeq r2) opSimpleSeq(SimpleEnumSeq s1);

    (FLByteEnum r1, FLByteEnum r2) opFLByte(FLByteEnum b1);
    (FLShortEnum r1, FLShortEnum r2) opFLShort(FLShortEnum s1);
    (FLUShortEnum r1, FLUShortEnum r2) opFLUShort(FLUShortEnum s1);
    (FLIntEnum r1, FLIntEnum r2) opFLInt(FLIntEnum i1);
    (FLUIntEnum r1, FLUIntEnum r2) opFLUInt(FLUIntEnum i1);
    (FLSimpleEnum r1, FLSimpleEnum r2) opFLSimple(FLSimpleEnum s1);

    (FLByteEnumSeq r1, FLByteEnumSeq r2) opFLByteSeq(FLByteEnumSeq b1);
    (FLShortEnumSeq r1, FLShortEnumSeq r2) opFLShortSeq(FLShortEnumSeq s1);
    (FLUShortEnumSeq r1, FLUShortEnumSeq r2) opFLUShortSeq(FLUShortEnumSeq s1);
    (FLIntEnumSeq r1, FLIntEnumSeq r2) opFLIntSeq(FLIntEnumSeq i1);
    (FLUIntEnumSeq r1, FLUIntEnumSeq r2) opFLUIntSeq(FLUIntEnumSeq i1);
    (FLSimpleEnumSeq r1, FLSimpleEnumSeq r2) opFLSimpleSeq(FLSimpleEnumSeq s1);

    (tag(1) ByteEnum? r1, tag(2) ByteEnum? r2) opTaggedByte(tag(1) ByteEnum? b1);
    (tag(1) FLByteEnum? r1, tag(2) FLByteEnum? r2) opTaggedFLByte(tag(1) FLByteEnum? b1);
    (tag(1) ByteEnumSeq? r1, tag(2) ByteEnumSeq? r2) opTaggedByteSeq(tag(1) ByteEnumSeq? b1);
    (tag(1) FLByteEnumSeq? r1, tag(2) FLByteEnumSeq? r2) opTaggedFLByteSeq(tag(1) FLByteEnumSeq? b1);
    (tag(1) FLIntEnumSeq? r1, tag(2) FLIntEnumSeq? r2) opTaggedFLIntSeq(tag(1) FLIntEnumSeq? i1);

    (MyFlags r1, MyFlags r2) opMyFlags(MyFlags f1);
    (MyFlagsSeq r1, MyFlagsSeq r2) opMyFlagsSeq(MyFlagsSeq f1);
    (tag(1) MyFlags? r1, tag(2) MyFlags? r2) opTaggedMyFlags(tag(1) MyFlags? f1);
    (tag(1) MyFlagsSeq? r1, tag(2) MyFlagsSeq? r2) opTaggedMyFlagsSeq(tag(1) MyFlagsSeq? f1);

    void shutdown();
}

}
