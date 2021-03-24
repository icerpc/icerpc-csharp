// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>
#include <StructTests.ice>

module IceRpc::Tests::ClientServer
{
    dictionary<byte, byte> ByteDict;
    dictionary<bool, bool> BoolDict;
    dictionary<short, short> ShortDict;
    dictionary<ushort, ushort> UShortDict;
    dictionary<int, int> IntDict;
    dictionary<varint, varint> VarIntDict;
    dictionary<uint, uint> UIntDict;
    dictionary<varuint, varuint> VarUIntDict;
    dictionary<long, long> LongDict;
    dictionary<varlong, varlong> VarLongDict;
    dictionary<ulong, ulong> ULongDict;
    dictionary<varulong, varulong> VarULongDict;
    dictionary<string, string> StringDict;

    dictionary<MyEnum, MyEnum> MyEnumDict;
    dictionary<MyStruct, MyStruct> MyStructDict;
    dictionary<string, Operations> OperationsDict;
    dictionary<string, AnotherStruct> AnotherStructDict;

    interface DictionaryOperations
    {
        // Builtin type dictionaries
        (ByteDict r1, ByteDict r2) opByteDict(ByteDict p1, ByteDict p2);
        (BoolDict r1, BoolDict r2) opBoolDict(BoolDict p1, BoolDict p2);
        (ShortDict r1, ShortDict r2) opShortDict(ShortDict p1, ShortDict p2);
        (UShortDict r1, UShortDict r2) opUShortDict(UShortDict p1, UShortDict p2);
        (IntDict r1, IntDict r2) opIntDict(IntDict p1, IntDict p2);
        (VarIntDict r1, VarIntDict r2) opVarIntDict(VarIntDict p1, VarIntDict p2);
        (UIntDict r1, UIntDict r2) opUIntDict(UIntDict p1, UIntDict p2);
        (VarUIntDict r1, VarUIntDict r2) opVarUIntDict(VarUIntDict p1, VarUIntDict p2);
        (LongDict r1, LongDict r2) opLongDict(LongDict p1, LongDict p2);
        (VarLongDict r1, VarLongDict r2) opVarLongDict(VarLongDict p1, VarLongDict p2);
        (ULongDict r1, ULongDict r2) opULongDict(ULongDict p1, ULongDict p2);
        (VarULongDict r1, VarULongDict r2) opVarULongDict(VarULongDict p1, VarULongDict p2);
        (StringDict r1, StringDict r2) opStringDict(StringDict p1, StringDict p2);

        // Defined types dictionaries
        (MyEnumDict r1, MyEnumDict r2) opMyEnumDict(MyEnumDict p1, MyEnumDict p2);
        (MyStructDict r1, MyStructDict r2) opMyStructDict(MyStructDict p1, MyStructDict p2);
        (OperationsDict r1, OperationsDict r2) opOperationsDict(OperationsDict p1, OperationsDict p2);
        (AnotherStructDict r1, AnotherStructDict r2) opAnotherStructDict(AnotherStructDict p1, AnotherStructDict p2);
    }
}
