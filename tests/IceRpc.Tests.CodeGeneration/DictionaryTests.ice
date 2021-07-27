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

    dictionary<byte, byte?> OptionalByteDict;
    dictionary<bool, bool?> OptionalBoolDict;
    dictionary<short, short?> OptionalShortDict;
    dictionary<ushort, ushort?> OptionalUShortDict;
    dictionary<int, int?> OptionalIntDict;
    dictionary<varint, varint?> OptionalVarIntDict;
    dictionary<uint, uint?> OptionalUIntDict;
    dictionary<varuint, varuint?> OptionalVarUIntDict;
    dictionary<long, long?> OptionalLongDict;
    dictionary<varlong, varlong?> OptionalVarLongDict;
    dictionary<ulong, ulong?> OptionalULongDict;
    dictionary<varulong, varulong?> OptionalVarULongDict;
    dictionary<string, string?> OptionalStringDict;

    dictionary<MyEnum, MyEnum> MyEnumDict;
    dictionary<MyFixedLengthEnum, MyFixedLengthEnum> MyFixedLengthEnumDict;
    dictionary<MyUncheckedEnum, MyUncheckedEnum> MyUncheckedEnumDict;
    dictionary<MyStruct, MyStruct> MyStructDict;
    dictionary<string, Operations> OperationsDict;
    dictionary<string, AnotherStruct> AnotherStructDict;

    dictionary<MyEnum, MyEnum?> OptionalMyEnumDict;
    dictionary<MyFixedLengthEnum, MyFixedLengthEnum?> OptionalMyFixedLengthEnumDict;
    dictionary<MyUncheckedEnum, MyUncheckedEnum?> OptionalMyUncheckedEnumDict;
    dictionary<MyStruct, MyStruct?> OptionalMyStructDict;
    dictionary<string, Operations?> OptionalOperationsDict;
    dictionary<string, AnotherStruct?> OptionalAnotherStructDict;

    [cs:generic(SortedDictionary)] dictionary<byte, byte> ByteSortedDict;
    [cs:generic(SortedDictionary)] dictionary<bool, bool> BoolSortedDict;
    [cs:generic(SortedDictionary)] dictionary<short, short> ShortSortedDict;
    [cs:generic(SortedDictionary)] dictionary<ushort, ushort> UShortSortedDict;
    [cs:generic(SortedDictionary)] dictionary<int, int> IntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<int, int?> OptionalIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varint, varint> VarIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<uint, uint> UIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varuint, varuint> VarUIntSortedDict;
    [cs:generic(SortedDictionary)] dictionary<long, long> LongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varlong, varlong> VarLongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<ulong, ulong> ULongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<varulong, varulong> VarULongSortedDict;
    [cs:generic(SortedDictionary)] dictionary<string, string> StringSortedDict;
    [cs:generic(SortedDictionary)] dictionary<string, string?> OptionalStringSortedDict;

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

        // Optional builtin type dictionaries
        (OptionalByteDict r1, OptionalByteDict r2) opOptionalByteDict(OptionalByteDict p1, OptionalByteDict p2);
        (OptionalBoolDict r1, OptionalBoolDict r2) opOptionalBoolDict(OptionalBoolDict p1, OptionalBoolDict p2);
        (OptionalShortDict r1, OptionalShortDict r2) opOptionalShortDict(OptionalShortDict p1, OptionalShortDict p2);
        (OptionalUShortDict r1, OptionalUShortDict r2) opOptionalUShortDict(
            OptionalUShortDict p1,
            OptionalUShortDict p2);
        (OptionalIntDict r1, OptionalIntDict r2) opOptionalIntDict(OptionalIntDict p1, OptionalIntDict p2);
        (OptionalVarIntDict r1, OptionalVarIntDict r2) opOptionalVarIntDict(
            OptionalVarIntDict p1,
            OptionalVarIntDict p2);
        (OptionalUIntDict r1, OptionalUIntDict r2) opOptionalUIntDict(OptionalUIntDict p1, OptionalUIntDict p2);
        (OptionalVarUIntDict r1, OptionalVarUIntDict r2) opOptionalVarUIntDict(
            OptionalVarUIntDict p1,
            OptionalVarUIntDict p2);
        (OptionalLongDict r1, OptionalLongDict r2) opOptionalLongDict(OptionalLongDict p1, OptionalLongDict p2);
        (OptionalVarLongDict r1, OptionalVarLongDict r2) opOptionalVarLongDict(
            OptionalVarLongDict p1,
            OptionalVarLongDict p2);
        (OptionalULongDict r1, OptionalULongDict r2) opOptionalULongDict(OptionalULongDict p1, OptionalULongDict p2);
        (OptionalVarULongDict r1, OptionalVarULongDict r2) opOptionalVarULongDict(
            OptionalVarULongDict p1,
            OptionalVarULongDict p2);
        (OptionalStringDict r1, OptionalStringDict r2) opOptionalStringDict(
            OptionalStringDict p1,
            OptionalStringDict p2);

        // Dictionaries with constructed types
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

        // Dictionaries with optional constructed types
        (OptionalMyEnumDict r1, OptionalMyEnumDict r2) opOptionalMyEnumDict(
            OptionalMyEnumDict p1,
            OptionalMyEnumDict p2);
        (OptionalMyFixedLengthEnumDict r1, OptionalMyFixedLengthEnumDict r2) opOptionalMyFixedLengthEnumDict(
            OptionalMyFixedLengthEnumDict p1,
            OptionalMyFixedLengthEnumDict p2);
        (OptionalMyUncheckedEnumDict r1, OptionalMyUncheckedEnumDict r2) opOptionalMyUncheckedEnumDict(
            OptionalMyUncheckedEnumDict p1,
            OptionalMyUncheckedEnumDict p2);
        (OptionalMyStructDict r1, OptionalMyStructDict r2) opOptionalMyStructDict(
            OptionalMyStructDict p1,
            OptionalMyStructDict p2);
        (OptionalOperationsDict r1, OptionalOperationsDict r2) opOptionalOperationsDict(
            OptionalOperationsDict p1,
            OptionalOperationsDict p2);
        (OptionalAnotherStructDict r1, OptionalAnotherStructDict r2) opOptionalAnotherStructDict(
            OptionalAnotherStructDict p1,
            OptionalAnotherStructDict p2);

        // Sorted dictionaries with builtin types
        (ByteSortedDict r1, ByteSortedDict r2) opByteSortedDict(ByteSortedDict p1, ByteSortedDict p2);
        (BoolSortedDict r1, BoolSortedDict r2) opBoolSortedDict(BoolSortedDict p1, BoolSortedDict p2);
        (ShortSortedDict r1, ShortSortedDict r2) opShortSortedDict(ShortSortedDict p1, ShortSortedDict p2);
        (UShortSortedDict r1, UShortSortedDict r2) opUShortSortedDict(UShortSortedDict p1, UShortSortedDict p2);
        (IntSortedDict r1, IntSortedDict r2) opIntSortedDict(IntSortedDict p1, IntSortedDict p2);
        (OptionalIntSortedDict r1, OptionalIntSortedDict r2) opOptionalIntSortedDict(
            OptionalIntSortedDict p1,
            OptionalIntSortedDict p2);
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
        (OptionalStringSortedDict r1, OptionalStringSortedDict r2) opOptionalStringSortedDict(
            OptionalStringSortedDict p1,
            OptionalStringSortedDict p2);

        // Sorted dictionaries with constructed types
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
