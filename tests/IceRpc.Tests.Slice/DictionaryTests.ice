// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    typealias ByteDict = dictionary<byte, byte>;
    typealias BoolDict = dictionary<bool, bool>;
    typealias ShortDict = dictionary<short, short>;
    typealias UShortDict = dictionary<ushort, ushort>;
    typealias IntDict = dictionary<int, int>;
    typealias VarIntDict = dictionary<varint, varint>;
    typealias UIntDict = dictionary<uint, uint>;
    typealias VarUIntDict = dictionary<varuint, varuint>;
    typealias LongDict = dictionary<long, long>;
    typealias VarLongDict = dictionary<varlong, varlong>;
    typealias ULongDict = dictionary<ulong, ulong>;
    typealias VarULongDict = dictionary<varulong, varulong>;
    typealias StringDict = dictionary<string, string>;

    typealias OptionalByteDict = dictionary<byte, byte?>;
    typealias OptionalBoolDict = dictionary<bool, bool?>;
    typealias OptionalShortDict = dictionary<short, short?>;
    typealias OptionalUShortDict = dictionary<ushort, ushort?>;
    typealias OptionalIntDict = dictionary<int, int?>;
    typealias OptionalVarIntDict = dictionary<varint, varint?>;
    typealias OptionalUIntDict = dictionary<uint, uint?>;
    typealias OptionalVarUIntDict = dictionary<varuint, varuint?>;
    typealias OptionalLongDict = dictionary<long, long?>;
    typealias OptionalVarLongDict = dictionary<varlong, varlong?>;
    typealias OptionalULongDict = dictionary<ulong, ulong?>;
    typealias OptionalVarULongDict = dictionary<varulong, varulong?>;
    typealias OptionalStringDict = dictionary<string, string?>;

    typealias MyEnumDict = dictionary<MyEnum, MyEnum>;
    typealias MyFixedLengthEnumDict = dictionary<MyFixedLengthEnum, MyFixedLengthEnum>;
    typealias MyUncheckedEnumDict = dictionary<MyUncheckedEnum, MyUncheckedEnum>;
    typealias MyStructDict = dictionary<MyStruct, MyStruct>;
    typealias OperationsDict = dictionary<string, Operations>;
    typealias AnotherStructDict = dictionary<string, AnotherStruct>;

    typealias OptionalMyEnumDict = dictionary<MyEnum, MyEnum?>;
    typealias OptionalMyFixedLengthEnumDict = dictionary<MyFixedLengthEnum, MyFixedLengthEnum?>;
    typealias OptionalMyUncheckedEnumDict = dictionary<MyUncheckedEnum, MyUncheckedEnum?>;
    typealias OptionalMyStructDict = dictionary<MyStruct, MyStruct?>;
    typealias OptionalOperationsDict = dictionary<string, Operations?>;
    typealias OptionalAnotherStructDict = dictionary<string, AnotherStruct?>;

    typealias ByteSortedDict = [cs:generic(SortedDictionary)] dictionary<byte, byte>;
    typealias BoolSortedDict = [cs:generic(SortedDictionary)] dictionary<bool, bool>;
    typealias ShortSortedDict = [cs:generic(SortedDictionary)] dictionary<short, short>;
    typealias UShortSortedDict = [cs:generic(SortedDictionary)] dictionary<ushort, ushort>;
    typealias IntSortedDict = [cs:generic(SortedDictionary)] dictionary<int, int>;
    typealias OptionalIntSortedDict = [cs:generic(SortedDictionary)] dictionary<int, int?>;
    typealias VarIntSortedDict = [cs:generic(SortedDictionary)] dictionary<varint, varint>;
    typealias UIntSortedDict = [cs:generic(SortedDictionary)] dictionary<uint, uint>;
    typealias VarUIntSortedDict = [cs:generic(SortedDictionary)] dictionary<varuint, varuint>;
    typealias LongSortedDict = [cs:generic(SortedDictionary)] dictionary<long, long>;
    typealias VarLongSortedDict = [cs:generic(SortedDictionary)] dictionary<varlong, varlong>;
    typealias ULongSortedDict = [cs:generic(SortedDictionary)] dictionary<ulong, ulong>;
    typealias VarULongSortedDict = [cs:generic(SortedDictionary)] dictionary<varulong, varulong>;
    typealias StringSortedDict = [cs:generic(SortedDictionary)] dictionary<string, string>;
    typealias OptionalStringSortedDict = [cs:generic(SortedDictionary)] dictionary<string, string?>;

    typealias MyEnumSortedDict = [cs:generic(SortedDictionary)] dictionary<MyEnum, MyEnum>;
    typealias MyFixedLengthEnumSortedDict = [cs:generic(SortedDictionary)] dictionary<MyFixedLengthEnum, MyFixedLengthEnum>;
    typealias MyUncheckedEnumSortedDict = [cs:generic(SortedDictionary)] dictionary<MyUncheckedEnum, MyUncheckedEnum>;

    interface DictionaryOperations
    {
        // Builtin type dictionaries
        opByteDict(p1: ByteDict, p2: ByteDict) -> (r1: ByteDict, r2: ByteDict);
        opBoolDict(p1: BoolDict, p2: BoolDict) -> (r1: BoolDict, r2: BoolDict);
        opShortDict(p1: ShortDict, p2: ShortDict) -> (r1: ShortDict, r2: ShortDict);
        opUShortDict(p1: UShortDict, p2: UShortDict) -> (r1: UShortDict, r2: UShortDict);
        opIntDict(p1: IntDict, p2: IntDict) -> (r1: IntDict, r2: IntDict);
        opVarIntDict(p1: VarIntDict, p2: VarIntDict) -> (r1: VarIntDict, r2: VarIntDict);
        opUIntDict(p1: UIntDict, p2: UIntDict) -> (r1: UIntDict, r2: UIntDict);
        opVarUIntDict(p1: VarUIntDict, p2: VarUIntDict) -> (r1: VarUIntDict, r2: VarUIntDict);
        opLongDict(p1: LongDict, p2: LongDict) -> (r1: LongDict, r2: LongDict);
        opVarLongDict(p1: VarLongDict, p2: VarLongDict) -> (r1: VarLongDict, r2: VarLongDict);
        opULongDict(p1: ULongDict, p2: ULongDict) -> (r1: ULongDict, r2: ULongDict);
        opVarULongDict(p1: VarULongDict, p2: VarULongDict) -> (r1: VarULongDict, r2: VarULongDict);
        opStringDict(p1: StringDict, p2: StringDict) -> (r1: StringDict, r2: StringDict);

        // Optional builtin type dictionaries
        opOptionalByteDict(p1: OptionalByteDict, p2: OptionalByteDict) -> (r1: OptionalByteDict, r2: OptionalByteDict);
        opOptionalBoolDict(p1: OptionalBoolDict, p2: OptionalBoolDict) -> (r1: OptionalBoolDict, r2: OptionalBoolDict);
        opOptionalShortDict(p1: OptionalShortDict, p2: OptionalShortDict) -> (
            r1: OptionalShortDict,
            r2: OptionalShortDict,
        );
        opOptionalUShortDict(p1: OptionalUShortDict, p2: OptionalUShortDict) -> (
            r1: OptionalUShortDict,
            r2: OptionalUShortDict,
        );
        opOptionalIntDict(p1: OptionalIntDict, p2: OptionalIntDict) -> (r1: OptionalIntDict, r2: OptionalIntDict);
        opOptionalVarIntDict(p1: OptionalVarIntDict, p2: OptionalVarIntDict) -> (
            r1: OptionalVarIntDict,
            r2: OptionalVarIntDict,
        );
        opOptionalUIntDict(p1: OptionalUIntDict, p2: OptionalUIntDict) -> (r1: OptionalUIntDict, r2: OptionalUIntDict);
        opOptionalVarUIntDict(p1: OptionalVarUIntDict, p2: OptionalVarUIntDict) -> (
            r1: OptionalVarUIntDict,
            r2: OptionalVarUIntDict,
        );
        opOptionalLongDict(p1: OptionalLongDict, p2: OptionalLongDict) -> (r1: OptionalLongDict, r2: OptionalLongDict);
        opOptionalVarLongDict(p1: OptionalVarLongDict, p2: OptionalVarLongDict) -> (
            r1: OptionalVarLongDict,
            r2: OptionalVarLongDict,
        );
        opOptionalULongDict(p1: OptionalULongDict, p2: OptionalULongDict) -> (
            r1: OptionalULongDict,
            r2: OptionalULongDict,
        );
        opOptionalVarULongDict(p1: OptionalVarULongDict, p2: OptionalVarULongDict) -> (
            r1: OptionalVarULongDict,
            r2: OptionalVarULongDict,
        );
        opOptionalStringDict(p1: OptionalStringDict, p2: OptionalStringDict) -> (
            r1: OptionalStringDict,
            r2: OptionalStringDict,
        );

        // Dictionaries with constructed types
        opMyEnumDict(p1: MyEnumDict, p2: MyEnumDict) -> (r1: MyEnumDict, r2: MyEnumDict);
        opMyFixedLengthEnumDict(p1: MyFixedLengthEnumDict, p2: MyFixedLengthEnumDict) -> (
            r1: MyFixedLengthEnumDict,
            r2: MyFixedLengthEnumDict,
        );
        opMyUncheckedEnumDict(p1: MyUncheckedEnumDict, p2: MyUncheckedEnumDict) -> (
            r1: MyUncheckedEnumDict,
            r2: MyUncheckedEnumDict,
        );
        opMyStructDict(p1: MyStructDict, p2: MyStructDict) -> (r1: MyStructDict, r2: MyStructDict);
        opOperationsDict(p1: OperationsDict, p2: OperationsDict) -> (r1: OperationsDict, r2: OperationsDict);
        opAnotherStructDict(p1: AnotherStructDict, p2: AnotherStructDict) -> (
            r1: AnotherStructDict,
            r2: AnotherStructDict,
        );

        // Dictionaries with optional constructed types
        opOptionalMyEnumDict(p1: OptionalMyEnumDict, p2: OptionalMyEnumDict) -> (
            r1: OptionalMyEnumDict,
            r2: OptionalMyEnumDict,
        );
        opOptionalMyFixedLengthEnumDict(p1: OptionalMyFixedLengthEnumDict, p2: OptionalMyFixedLengthEnumDict) -> (
            r1: OptionalMyFixedLengthEnumDict,
            r2: OptionalMyFixedLengthEnumDict,
        );
        opOptionalMyUncheckedEnumDict(p1: OptionalMyUncheckedEnumDict, p2: OptionalMyUncheckedEnumDict) -> (
            r1: OptionalMyUncheckedEnumDict,
            r2: OptionalMyUncheckedEnumDict,
        );
        opOptionalMyStructDict(p1: OptionalMyStructDict, p2: OptionalMyStructDict) -> (
            r1: OptionalMyStructDict,
            r2: OptionalMyStructDict,
        );
        opOptionalOperationsDict(p1: OptionalOperationsDict, p2: OptionalOperationsDict) -> (
            r1: OptionalOperationsDict,
            r2: OptionalOperationsDict,
        );
        opOptionalAnotherStructDict(p1: OptionalAnotherStructDict, p2: OptionalAnotherStructDict) -> (
            r1: OptionalAnotherStructDict,
            r2: OptionalAnotherStructDict,
        );

        // Sorted dictionaries with builtin types
        opByteSortedDict(p1: ByteSortedDict, p2: ByteSortedDict) -> (r1: ByteSortedDict, r2: ByteSortedDict);
        opBoolSortedDict(p1: BoolSortedDict, p2: BoolSortedDict) -> (r1: BoolSortedDict, r2: BoolSortedDict);
        opShortSortedDict(p1: ShortSortedDict, p2: ShortSortedDict) -> (r1: ShortSortedDict, r2: ShortSortedDict);
        opUShortSortedDict(p1: UShortSortedDict, p2: UShortSortedDict) -> (r1: UShortSortedDict, r2: UShortSortedDict);
        opIntSortedDict(p1: IntSortedDict, p2: IntSortedDict) -> (r1: IntSortedDict, r2: IntSortedDict);
        opOptionalIntSortedDict(p1: OptionalIntSortedDict, p2: OptionalIntSortedDict) -> (
            r1: OptionalIntSortedDict,
            r2: OptionalIntSortedDict,
        );
        opVarIntSortedDict(p1: VarIntSortedDict, p2: VarIntSortedDict) -> (r1: VarIntSortedDict, r2: VarIntSortedDict);
        opUIntSortedDict(p1: UIntSortedDict, p2: UIntSortedDict) -> (r1: UIntSortedDict, r2: UIntSortedDict);
        opVarUIntSortedDict(p1: VarUIntSortedDict, p2: VarUIntSortedDict) -> (
            r1: VarUIntSortedDict,
            r2: VarUIntSortedDict,
        );
        opLongSortedDict(p1: LongSortedDict, p2: LongSortedDict) -> (r1: LongSortedDict, r2: LongSortedDict);
        opVarLongSortedDict(p1: VarLongSortedDict, p2: VarLongSortedDict) -> (
            r1: VarLongSortedDict,
            r2: VarLongSortedDict,
        );
        opULongSortedDict(p1: ULongSortedDict, p2: ULongSortedDict) -> (r1: ULongSortedDict, r2: ULongSortedDict);
        opVarULongSortedDict(p1: VarULongSortedDict, p2: VarULongSortedDict) -> (
            r1: VarULongSortedDict,
            r2: VarULongSortedDict,
        );
        opStringSortedDict(p1: StringSortedDict, p2: StringSortedDict) -> (r1: StringSortedDict, r2: StringSortedDict);
        opOptionalStringSortedDict(p1: OptionalStringSortedDict, p2: OptionalStringSortedDict) -> (
            r1: OptionalStringSortedDict,
            r2: OptionalStringSortedDict,
        );

        // Sorted dictionaries with constructed types
        opMyEnumSortedDict(p1: MyEnumSortedDict, p2: MyEnumSortedDict) -> (r1: MyEnumSortedDict, r2: MyEnumSortedDict);
        opMyFixedLengthEnumSortedDict(p1: MyFixedLengthEnumSortedDict, p2: MyFixedLengthEnumSortedDict) -> (
            r1: MyFixedLengthEnumSortedDict,
            r2: MyFixedLengthEnumSortedDict,
        );
        opMyUncheckedEnumSortedDict(p1: MyUncheckedEnumSortedDict, p2: MyUncheckedEnumSortedDict) -> (
            r1: MyUncheckedEnumSortedDict,
            r2: MyUncheckedEnumSortedDict,
        );
        // TODO Allow structs as sorted dictionary keys, implementing IComparable
    }
}
