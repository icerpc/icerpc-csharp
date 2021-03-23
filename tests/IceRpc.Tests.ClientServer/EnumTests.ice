// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    enum MyEnum
    {
        enum1,
        enum2,
        enum3 = 10,
        enum4,
        enum5 = 20,
        enum6,
        enum7 = 30,
        enum8,
        enum9 = 40,
        enum10,
        enum11 = 226
    }
    sequence<MyEnum> MyEnumSeq;

    enum MyFixedLengthEnum : short
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
    sequence<MyFixedLengthEnum> MyFixedLengthEnumSeq;

    [cs:attribute(System.Flags)] unchecked enum MyUncheckedEnum : uint
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
    }
    sequence<MyUncheckedEnum> MyUncheckedEnumSeq;

    interface EnumService
    {
        (MyEnum r1, MyEnum r2) opMyEnum(MyEnum p1, MyEnum p2);
        (MyEnumSeq r1, MyEnumSeq r2) opMyEnum(MyEnumSeq p1, MyEnumSeq p2);
        (MyFixedLengthEnum r1, MyFixedLengthEnum r2) opMyFixedLengthEnum(MyFixedLengthEnum p1, MyFixedLengthEnum p2);
        (MyFixedLengthEnumSeq r1, MyFixedLengthEnumSeq r2) opMyFixedLengthEnum(
            MyFixedLengthEnumSeq p1,
            MyFixedLengthEnumSeq p2);
        (MyUncheckedEnum r1, MyUncheckedEnum r2) opMyUncheckedEnum(MyUncheckedEnum p1, MyUncheckedEnum p2);
        (MyUncheckedEnumSeq r1, MyUncheckedEnumSeq r2) opMyUncheckedEnum(MyUncheckedEnumSeq p1, MyUncheckedEnumSeq p2);
    }
}