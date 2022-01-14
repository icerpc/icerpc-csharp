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

    typealias ByteCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<byte, byte>;
    typealias BoolCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<bool, bool>;
    typealias ShortCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<short, short>;
    typealias UShortCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<ushort, ushort>;
    typealias IntCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<int, int>;
    typealias OptionalIntCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<int, int?>;
    typealias VarIntCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<varint, varint>;
    typealias UIntCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<uint, uint>;
    typealias VarUIntCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<varuint, varuint>;
    typealias LongCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<long, long>;
    typealias VarLongCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<varlong, varlong>;
    typealias ULongCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<ulong, ulong>;
    typealias VarULongCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<varulong, varulong>;
    typealias StringCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<string, string>;
    typealias OptionalStringCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<string, string?>;

    typealias MyEnumCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<MyEnum, MyEnum>;
    typealias MyFixedLengthEnumCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<MyFixedLengthEnum, MyFixedLengthEnum>;
    typealias MyUncheckedEnumCustomDict = [cs:generic("IceRpc.Tests.Slice.CustomDictionary")] dictionary<MyUncheckedEnum, MyUncheckedEnum>;

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
        opByteCustomDict(p1: ByteCustomDict, p2: ByteCustomDict) -> (r1: ByteCustomDict, r2: ByteCustomDict);
        opBoolCustomDict(p1: BoolCustomDict, p2: BoolCustomDict) -> (r1: BoolCustomDict, r2: BoolCustomDict);
        opShortCustomDict(p1: ShortCustomDict, p2: ShortCustomDict) -> (r1: ShortCustomDict, r2: ShortCustomDict);
        opUShortCustomDict(p1: UShortCustomDict, p2: UShortCustomDict) -> (r1: UShortCustomDict, r2: UShortCustomDict);
        opIntCustomDict(p1: IntCustomDict, p2: IntCustomDict) -> (r1: IntCustomDict, r2: IntCustomDict);
        opOptionalIntCustomDict(p1: OptionalIntCustomDict, p2: OptionalIntCustomDict) -> (
            r1: OptionalIntCustomDict,
            r2: OptionalIntCustomDict,
        );
        opVarIntCustomDict(p1: VarIntCustomDict, p2: VarIntCustomDict) -> (r1: VarIntCustomDict, r2: VarIntCustomDict);
        opUIntCustomDict(p1: UIntCustomDict, p2: UIntCustomDict) -> (r1: UIntCustomDict, r2: UIntCustomDict);
        opVarUIntCustomDict(p1: VarUIntCustomDict, p2: VarUIntCustomDict) -> (
            r1: VarUIntCustomDict,
            r2: VarUIntCustomDict,
        );
        opLongCustomDict(p1: LongCustomDict, p2: LongCustomDict) -> (r1: LongCustomDict, r2: LongCustomDict);
        opVarLongCustomDict(p1: VarLongCustomDict, p2: VarLongCustomDict) -> (
            r1: VarLongCustomDict,
            r2: VarLongCustomDict,
        );
        opULongCustomDict(p1: ULongCustomDict, p2: ULongCustomDict) -> (r1: ULongCustomDict, r2: ULongCustomDict);
        opVarULongCustomDict(p1: VarULongCustomDict, p2: VarULongCustomDict) -> (
            r1: VarULongCustomDict,
            r2: VarULongCustomDict,
        );
        opStringCustomDict(p1: StringCustomDict, p2: StringCustomDict) -> (r1: StringCustomDict, r2: StringCustomDict);
        opOptionalStringCustomDict(p1: OptionalStringCustomDict, p2: OptionalStringCustomDict) -> (
            r1: OptionalStringCustomDict,
            r2: OptionalStringCustomDict,
        );

        // Sorted dictionaries with constructed types
        opMyEnumCustomDict(p1: MyEnumCustomDict, p2: MyEnumCustomDict) -> (r1: MyEnumCustomDict, r2: MyEnumCustomDict);
        opMyFixedLengthEnumCustomDict(p1: MyFixedLengthEnumCustomDict, p2: MyFixedLengthEnumCustomDict) -> (
            r1: MyFixedLengthEnumCustomDict,
            r2: MyFixedLengthEnumCustomDict,
        );
        opMyUncheckedEnumCustomDict(p1: MyUncheckedEnumCustomDict, p2: MyUncheckedEnumCustomDict) -> (
            r1: MyUncheckedEnumCustomDict,
            r2: MyUncheckedEnumCustomDict,
        );
        // TODO Allow structs as sorted dictionary keys, implementing IComparable
    }
}
