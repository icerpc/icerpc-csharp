// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>
#include <StructTests.ice>

module IceRpc::Tests::CodeGeneration
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
    dictionary<MyFixedLengthEnum, MyFixedLengthEnum> MyFixedLengthEnumDict;
    dictionary<MyUncheckedEnum, MyUncheckedEnum> MyUncheckedEnumDict;
    dictionary<MyStruct, MyStruct> MyStructDict;
    dictionary<string, Operations> OperationsDict;
    dictionary<string, AnotherStruct> AnotherStructDict;


    [cs:generic(SortedDictionary)] dictionary<byte, byte> ByteSortedDict;
    [cs:generic(SortedDictionary)] dictionary<bool, bool> BoolSortedDict;
    [cs:generic(SortedDictionary)] dictionary<short, short> ShortSortedDict;
    [cs:generic(SortedDictionary)] dictionary<ushort, ushort> UShortSortedDict;
    [cs:generic(SortedDictionary)] dictionary<int, int> IntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varint, varint> VarIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<uint, uint> UIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varuint, varuint> VarUIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<long, long> LongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varlong, varlong> VarLongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<ulong, ulong> ULongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varulong, varulong> VarULongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<string, string> StringSortedDict;

    [cs:generic(SortedDictionary)] dictionary<MyEnum, MyEnum> MyEnumSortedDict;
    [cs:generic(SortedDictionary)] dictionary<MyFixedLengthEnum, MyFixedLengthEnum> MyFixedLengthEnumSortedDict;
    [cs:generic(SortedDictionary)] dictionary<MyUncheckedEnum, MyUncheckedEnum> MyUncheckedEnumSortedDict;

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
        (MyFixedLengthEnumDict r1, MyFixedLengthEnumDict r2) opMyFixedLengthEnumDict(
            MyFixedLengthEnumDict p1,
            MyFixedLengthEnumDict p2);
        (MyUncheckedEnumDict r1, MyUncheckedEnumDict r2) opMyUncheckedEnumDict(
            MyUncheckedEnumDict p1,
            MyUncheckedEnumDict p2);
        (MyStructDict r1, MyStructDict r2) opMyStructDict(MyStructDict p1, MyStructDict p2);
        (OperationsDict r1, OperationsDict r2) opOperationsDict(OperationsDict p1, OperationsDict p2);
        (AnotherStructDict r1, AnotherStructDict r2) opAnotherStructDict(AnotherStructDict p1, AnotherStructDict p2);

        // Builtin type sorted dictionaries
        (ByteSortedDict r1, ByteSortedDict r2) opByteSortedDict(ByteSortedDict p1, ByteSortedDict p2);
        (BoolSortedDict r1, BoolSortedDict r2) opBoolSortedDict(BoolSortedDict p1, BoolSortedDict p2);
        (ShortSortedDict r1, ShortSortedDict r2) opShortSortedDict(ShortSortedDict p1, ShortSortedDict p2);
        (UShortSortedDict r1, UShortSortedDict r2) opUShortSortedDict(UShortSortedDict p1, UShortSortedDict p2);
        (IntSortedDict r1, IntSortedDict r2) opIntSortedDict(IntSortedDict p1, IntSortedDict p2);
        (VarIntSortedDict r1, VarIntSortedDict r2) opVarIntSortedDict(VarIntSortedDict p1, VarIntSortedDict p2);
        (UIntSortedDict r1, UIntSortedDict r2) opUIntSortedDict(UIntSortedDict p1, UIntSortedDict p2);
        (VarUIntSortedDict r1, VarUIntSortedDict r2) opVarUIntSortedDict(VarUIntSortedDict p1, VarUIntSortedDict p2);
        (LongSortedDict r1, LongSortedDict r2) opLongSortedDict(LongSortedDict p1, LongSortedDict p2);
        (VarLongSortedDict r1, VarLongSortedDict r2) opVarLongSortedDict(VarLongSortedDict p1, VarLongSortedDict p2);
        (ULongSortedDict r1, ULongSortedDict r2) opULongSortedDict(ULongSortedDict p1, ULongSortedDict p2);
        (VarULongSortedDict r1, VarULongSortedDict r2) opVarULongSortedDict(
            VarULongSortedDict p1,
            VarULongSortedDict p2);
        (StringSortedDict r1, StringSortedDict r2) opStringSortedDict(StringSortedDict p1, StringSortedDict p2);

        // Defined types sorted dictionaries
        (MyEnumSortedDict r1, MyEnumSortedDict r2) opMyEnumSortedDict(MyEnumSortedDict p1, MyEnumSortedDict p2);
        (MyFixedLengthEnumSortedDict r1, MyFixedLengthEnumSortedDict r2) opMyFixedLengthEnumSortedDict(
            MyFixedLengthEnumSortedDict p1,
            MyFixedLengthEnumSortedDict p2);
        (MyUncheckedEnumSortedDict r1, MyUncheckedEnumSortedDict r2) opMyUncheckedEnumSortedDict(
            MyUncheckedEnumSortedDict p1,
            MyUncheckedEnumSortedDict p2);
        // TODO Allow structs as sorted dictionary keys, implementing IComparable
    }
}
