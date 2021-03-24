// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>
#include <StructTests.ice>

module IceRpc::Tests::ClientServer
{
    sequence<byte> ByteSeq;
    sequence<bool> BoolSeq;
    sequence<short> ShortSeq;
    sequence<ushort> UShortSeq;
    sequence<int> IntSeq;
    sequence<varint> VarIntSeq;
    sequence<uint> UIntSeq;
    sequence<varuint> VarUIntSeq;
    sequence<long> LongSeq;
    sequence<varlong> VarLongSeq;
    sequence<ulong> ULongSeq;
    sequence<varulong> VarULongSeq;
    sequence<float> FloatSeq;
    sequence<double> DoubleSeq;
    sequence<string> StringSeq;

    sequence<MyEnum> MyEnumSeq;
    sequence<MyFixedLengthEnum> MyFixedLengthEnumSeq;
    sequence<MyUncheckedEnum> MyUncheckedEnumSeq;

    sequence<MyStruct> MyStructSeq;
    sequence<Operations> OperationsSeq;
    sequence<AnotherStruct> AnotherStructSeq;

    interface SequenceOperations
    {
        // Builtin types sequences
        (ByteSeq r1, ByteSeq r2) opByteSeq(ByteSeq p1, ByteSeq p2);
        (BoolSeq r1, BoolSeq r2) opBoolSeq(BoolSeq p1, BoolSeq p2);
        (ShortSeq r1, ShortSeq r2) opShortSeq(ShortSeq p1, ShortSeq p2);
        (UShortSeq r1, UShortSeq r2) opUShortSeq(UShortSeq p1, UShortSeq p2);
        (IntSeq r1, IntSeq r2) opIntSeq(IntSeq p1, IntSeq p2);
        (VarIntSeq r1, VarIntSeq r2) opVarIntSeq(VarIntSeq p1, VarIntSeq p2);
        (UIntSeq r1, UIntSeq r2) opUIntSeq(UIntSeq p1, UIntSeq p2);
        (VarUIntSeq r1, VarUIntSeq r2) opVarUIntSeq(VarUIntSeq p1, VarUIntSeq p2);
        (LongSeq r1, LongSeq r2) opLongSeq(LongSeq p1, LongSeq p2);
        (VarLongSeq r1, VarLongSeq r2) opVarLongSeq(VarLongSeq p1, VarLongSeq p2);
        (ULongSeq r1, ULongSeq r2) opULongSeq(ULongSeq p1, ULongSeq p2);
        (VarULongSeq r1, VarULongSeq r2) opVarULongSeq(VarULongSeq p1, VarULongSeq p2);
        (FloatSeq r1, FloatSeq r2) opFloatSeq(FloatSeq p1, FloatSeq p2);
        (DoubleSeq r1, DoubleSeq r2) opDoubleSeq(DoubleSeq p1, DoubleSeq p2);
        (StringSeq r1, StringSeq r2) opStringSeq(StringSeq p1, StringSeq p2);

        // Defined types sequences
        (MyEnumSeq r1, MyEnumSeq r2) opMyEnumSeq(MyEnumSeq p1, MyEnumSeq p2);
        (MyFixedLengthEnumSeq r1, MyFixedLengthEnumSeq r2) opMyFixedLengthEnumSeq(
            MyFixedLengthEnumSeq p1,
            MyFixedLengthEnumSeq p2);
        (MyUncheckedEnumSeq r1, MyUncheckedEnumSeq r2) opMyUncheckedEnumSeq(
            MyUncheckedEnumSeq p1,
            MyUncheckedEnumSeq p2);
        (MyStructSeq r1, MyStructSeq r2) opMyStructSeq(MyStructSeq p1, MyStructSeq p2);
        (Operations r1, Operations r2) opOperationsSeq(Operations p1, Operations p2);
        (AnotherStructSeq r1, AnotherStructSeq r2) opAnotherStructSeq(AnotherStructSeq p1, AnotherStructSeq p2);
    }
}
