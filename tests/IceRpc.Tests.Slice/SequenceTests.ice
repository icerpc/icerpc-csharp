// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    typealias ByteSeq = sequence<byte>;
    typealias BoolSeq = sequence<bool>;
    typealias ShortSeq = sequence<short>;
    typealias UShortSeq = sequence<ushort>;
    typealias IntSeq = sequence<int>;
    typealias VarIntSeq = sequence<varint>;
    typealias UIntSeq = sequence<uint>;
    typealias VarUIntSeq = sequence<varuint>;
    typealias LongSeq = sequence<long>;
    typealias VarLongSeq = sequence<varlong>;
    typealias ULongSeq = sequence<ulong>;
    typealias VarULongSeq = sequence<varulong>;
    typealias FloatSeq = sequence<float>;
    typealias DoubleSeq = sequence<double>;
    typealias StringSeq = sequence<string>;

    typealias OptionalByteSeq = sequence<byte?>;
    typealias OptionalBoolSeq = sequence<bool?>;
    typealias OptionalShortSeq = sequence<short?>;
    typealias OptionalUShortSeq = sequence<ushort?>;
    typealias OptionalIntSeq = sequence<int?>;
    typealias OptionalVarIntSeq = sequence<varint?>;
    typealias OptionalUIntSeq = sequence<uint?>;
    typealias OptionalVarUIntSeq = sequence<varuint?>;
    typealias OptionalLongSeq = sequence<long?>;
    typealias OptionalVarLongSeq = sequence<varlong?>;
    typealias OptionalULongSeq = sequence<ulong?>;
    typealias OptionalVarULongSeq = sequence<varulong?>;
    typealias OptionalFloatSeq = sequence<float?>;
    typealias OptionalDoubleSeq = sequence<double?>;
    typealias OptionalStringSeq = sequence<string?>;

    typealias MyEnumSeq = sequence<MyEnum>;
    typealias MyFixedLengthEnumSeq = sequence<MyFixedLengthEnum>;
    typealias MyUncheckedEnumSeq = sequence<MyUncheckedEnum>;

    typealias MyStructSeq = sequence<MyStruct>;
    typealias OperationsSeq = sequence<Operations>;
    typealias AnotherStructSeq = sequence<AnotherStruct>;

    typealias OptionalMyEnumSeq = sequence<MyEnum?>;
    typealias OptionalMyFixedLengthEnumSeq = sequence<MyFixedLengthEnum?>;
    typealias OptionalMyUncheckedEnumSeq = sequence<MyUncheckedEnum?>;

    typealias OptionalMyStructSeq = sequence<MyStruct?>;
    typealias OptionalOperationsSeq = sequence<Operations?>;
    typealias OptionalAnotherStructSeq = sequence<AnotherStruct?>;

    // Sequence mapping
    typealias ByteList = [cs:generic("System.Collections.Generic.List")] sequence<byte>;
    typealias ByteCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<byte>;

    typealias BoolList = [cs:generic("System.Collections.Generic.List")] sequence<bool>;
    typealias BoolCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<bool>;

    typealias ShortList = [cs:generic("System.Collections.Generic.List")] sequence<short>;
    typealias ShortCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<short>;

    typealias IntList = [cs:generic("System.Collections.Generic.List")] sequence<int>;
    typealias IntCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<int>;

    typealias LongList = [cs:generic("System.Collections.Generic.List")] sequence<long>;
    typealias LongCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<long>;

    typealias FloatList = [cs:generic("System.Collections.Generic.List")] sequence<float>;
    typealias FloatCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<float>;

    typealias DoubleList = [cs:generic("System.Collections.Generic.List")] sequence<double>;
    typealias DoubleCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<double>;

    typealias StringList = [cs:generic("System.Collections.Generic.List")] sequence<string>;
    typealias StringCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<string>;

    typealias MyEnumList = [cs:generic("System.Collections.Generic.List")] sequence<MyEnum>;
    typealias MyEnumCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<MyEnum>;

    typealias MyFixedLengthEnumList = [cs:generic("System.Collections.Generic.List")] sequence<MyFixedLengthEnum>;
    typealias MyFixedLengthEnumCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<MyFixedLengthEnum>;

    typealias MyUncheckedEnumList = [cs:generic("System.Collections.Generic.List")] sequence<MyUncheckedEnum>;
    typealias MyUncheckedEnumCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<MyUncheckedEnum>;

    typealias MyStructList = [cs:generic("System.Collections.Generic.List")] sequence<MyStruct>;
    typealias MyStructCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<MyStruct>;

    typealias OperationsList = [cs:generic("System.Collections.Generic.List")] sequence<Operations>;
    typealias OperationsCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<Operations>;

    typealias AnotherStructList = [cs:generic("System.Collections.Generic.List")] sequence<AnotherStruct>;
    typealias AnotherStructCustomSeq = [cs:generic("IceRpc.Tests.Slice.CustomSequence")] sequence<AnotherStruct>;

    interface SequenceOperations
    {
        // Builtin types sequences
        opByteSeq(p1: ByteSeq, p2: ByteSeq) -> (r1: ByteSeq, r2: ByteSeq);
        opBoolSeq(p1: BoolSeq, p2: BoolSeq) -> (r1: BoolSeq, r2: BoolSeq);
        opShortSeq(p1: ShortSeq, p2: ShortSeq) -> (r1: ShortSeq, r2: ShortSeq);
        opUShortSeq(p1: UShortSeq, p2: UShortSeq) -> (r1: UShortSeq, r2: UShortSeq);
        opIntSeq(p1: IntSeq, p2: IntSeq) -> (r1: IntSeq, r2: IntSeq);
        opVarIntSeq(p1: VarIntSeq, p2: VarIntSeq) -> (r1: VarIntSeq, r2: VarIntSeq);
        opUIntSeq(p1: UIntSeq, p2: UIntSeq) -> (r1: UIntSeq, r2: UIntSeq);
        opVarUIntSeq(p1: VarUIntSeq, p2: VarUIntSeq) -> (r1: VarUIntSeq, r2: VarUIntSeq);
        opLongSeq(p1: LongSeq, p2: LongSeq) -> (r1: LongSeq, r2: LongSeq);
        opVarLongSeq(p1: VarLongSeq, p2: VarLongSeq) -> (r1: VarLongSeq, r2: VarLongSeq);
        opULongSeq(p1: ULongSeq, p2: ULongSeq) -> (r1: ULongSeq, r2: ULongSeq);
        opVarULongSeq(p1: VarULongSeq, p2: VarULongSeq) -> (r1: VarULongSeq, r2: VarULongSeq);
        opFloatSeq(p1: FloatSeq, p2: FloatSeq) -> (r1: FloatSeq, r2: FloatSeq);
        opDoubleSeq(p1: DoubleSeq, p2: DoubleSeq) -> (r1: DoubleSeq, r2: DoubleSeq);
        opStringSeq(p1: StringSeq, p2: StringSeq) -> (r1: StringSeq, r2: StringSeq);

        // Optional builtin types sequences
        opOptionalByteSeq(p1: OptionalByteSeq, p2: OptionalByteSeq) -> (r1: OptionalByteSeq, r2: OptionalByteSeq);
        opOptionalBoolSeq(p1: OptionalBoolSeq, p2: OptionalBoolSeq) -> (r1: OptionalBoolSeq, r2: OptionalBoolSeq);
        opOptionalShortSeq(p1: OptionalShortSeq, p2: OptionalShortSeq) -> (r1: OptionalShortSeq, r2: OptionalShortSeq);
        opOptionalUShortSeq(p1: OptionalUShortSeq, p2: OptionalUShortSeq) -> (
            r1: OptionalUShortSeq,
            r2: OptionalUShortSeq,
        );
        opOptionalIntSeq(p1: OptionalIntSeq, p2: OptionalIntSeq) -> (r1: OptionalIntSeq, r2: OptionalIntSeq);
        opOptionalVarIntSeq(p1: OptionalVarIntSeq, p2: OptionalVarIntSeq) -> (
            r1: OptionalVarIntSeq,
            r2: OptionalVarIntSeq,
        );
        opOptionalUIntSeq(p1: OptionalUIntSeq, p2: OptionalUIntSeq) -> (r1: OptionalUIntSeq, r2: OptionalUIntSeq);
        opOptionalVarUIntSeq(p1: OptionalVarUIntSeq, p2: OptionalVarUIntSeq) -> (
            r1: OptionalVarUIntSeq,
            r2: OptionalVarUIntSeq,
        );
        opOptionalLongSeq(p1: OptionalLongSeq, p2: OptionalLongSeq) -> (r1: OptionalLongSeq, r2: OptionalLongSeq);
        opOptionalVarLongSeq(p1: OptionalVarLongSeq, p2: OptionalVarLongSeq) -> (
            r1: OptionalVarLongSeq,
            r2: OptionalVarLongSeq,
        );
        opOptionalULongSeq(p1: OptionalULongSeq, p2: OptionalULongSeq) -> (
            r1: OptionalULongSeq,
            r2: OptionalULongSeq,
        );
        opOptionalVarULongSeq(p1: OptionalVarULongSeq, p2: OptionalVarULongSeq) -> (
            r1: OptionalVarULongSeq,
            r2: OptionalVarULongSeq,
        );
        opOptionalFloatSeq(p1: OptionalFloatSeq, p2: OptionalFloatSeq) -> (r1: OptionalFloatSeq, r2: OptionalFloatSeq);
        opOptionalDoubleSeq(p1: OptionalDoubleSeq, p2: OptionalDoubleSeq) -> (
            r1: OptionalDoubleSeq,
            r2: OptionalDoubleSeq,
        );
        opOptionalStringSeq(p1: OptionalStringSeq, p2: OptionalStringSeq) -> (
            r1: OptionalStringSeq,
            r2: OptionalStringSeq,
        );

        // Defined types sequences
        opMyEnumSeq(p1: MyEnumSeq, p2: MyEnumSeq) -> (r1: MyEnumSeq, r2: MyEnumSeq);
        opMyFixedLengthEnumSeq(p1: MyFixedLengthEnumSeq, p2: MyFixedLengthEnumSeq) -> (
            r1: MyFixedLengthEnumSeq,
            r2: MyFixedLengthEnumSeq,
        );
        opMyUncheckedEnumSeq(p1: MyUncheckedEnumSeq, p2: MyUncheckedEnumSeq) -> (
            r1: MyUncheckedEnumSeq,
            r2: MyUncheckedEnumSeq,
        );
        opMyStructSeq(p1: MyStructSeq, p2: MyStructSeq) -> (r1: MyStructSeq, r2: MyStructSeq);
        opOperationsSeq(p1: OperationsSeq, p2: OperationsSeq) -> (r1: OperationsSeq, r2: OperationsSeq);
        opAnotherStructSeq(p1: AnotherStructSeq, p2: AnotherStructSeq) -> (r1: AnotherStructSeq, r2: AnotherStructSeq);

        // Optional defined types sequences
        opOptionalMyEnumSeq(p1: OptionalMyEnumSeq, p2: OptionalMyEnumSeq) -> (
            r1: OptionalMyEnumSeq,
            r2: OptionalMyEnumSeq,
        );
        opOptionalMyFixedLengthEnumSeq(p1: OptionalMyFixedLengthEnumSeq, p2: OptionalMyFixedLengthEnumSeq) -> (
            r1: OptionalMyFixedLengthEnumSeq,
            r2: OptionalMyFixedLengthEnumSeq,
        );
        opOptionalMyUncheckedEnumSeq(p1: OptionalMyUncheckedEnumSeq, p2: OptionalMyUncheckedEnumSeq) -> (
            r1: OptionalMyUncheckedEnumSeq,
            r2: OptionalMyUncheckedEnumSeq,
        );
        opOptionalMyStructSeq(p1: OptionalMyStructSeq, p2: OptionalMyStructSeq) -> (
            r1: OptionalMyStructSeq,
            r2: OptionalMyStructSeq,
        );
        opOptionalOperationsSeq(p1: OptionalOperationsSeq, p2: OptionalOperationsSeq) -> (
            r1: OptionalOperationsSeq,
            r2: OptionalOperationsSeq,
        );
        opOptionalAnotherStructSeq(p1: OptionalAnotherStructSeq, p2: OptionalAnotherStructSeq) -> (
            r1: OptionalAnotherStructSeq,
            r2: OptionalAnotherStructSeq,
        );

        // Sequence mapping
        opByteList(p1: ByteList, p2: ByteList) -> (r1: ByteList, r2: ByteList);
        opByteCustomSeq(p1: ByteCustomSeq, p2: ByteCustomSeq) -> (r1: ByteCustomSeq, r2: ByteCustomSeq);

        opBoolList(p1: BoolList, p2: BoolList) -> (r1: BoolList, r2: BoolList);
        opBoolCustomSeq(p1: BoolCustomSeq, p2: BoolCustomSeq) -> (r1: BoolCustomSeq, r2: BoolCustomSeq);

        opIntList(p1: IntList, p2: IntList) -> (r1: IntList, r2: IntList);
        opIntCustomSeq(p1: IntCustomSeq, p2: IntCustomSeq) -> (r1: IntCustomSeq, r2: IntCustomSeq);

        opLongList(p1: LongList, p2: LongList) -> (r1: LongList, r2: LongList);
        opLongCustomSeq(p1: LongCustomSeq, p2: LongCustomSeq) -> (r1: LongCustomSeq, r2: LongCustomSeq);

        opFloatList(p1: FloatList, p2: FloatList) -> (r1: FloatList, r2: FloatList);
        opFloatCustomSeq(p1: FloatCustomSeq, p2: FloatCustomSeq) -> (r1: FloatCustomSeq, r2: FloatCustomSeq);

        opStringList(p1: StringList, p2: StringList) -> (r1: StringList, r2: StringList);
        opStringCustomSeq(p1: StringCustomSeq, p2: StringCustomSeq) -> (r1: StringCustomSeq, r2: StringCustomSeq);

        opMyEnumList(p1: MyEnumList, p2: MyEnumList) -> (r1: MyEnumList, r2: MyEnumList);
        opMyEnumCustomSeq(p1: MyEnumCustomSeq, p2: MyEnumCustomSeq) -> (r1: MyEnumCustomSeq, r2: MyEnumCustomSeq);

        opMyFixedLengthEnumList(p1: MyFixedLengthEnumList, p2: MyFixedLengthEnumList) -> (
            r1: MyFixedLengthEnumList,
            r2: MyFixedLengthEnumList,
        );
        opMyFixedLengthEnumCustomSeq(p1: MyFixedLengthEnumCustomSeq, p2: MyFixedLengthEnumCustomSeq) -> (
            r1: MyFixedLengthEnumCustomSeq,
            r2: MyFixedLengthEnumCustomSeq,
        );

        opMyUncheckedEnumList(p1: MyUncheckedEnumList, p2: MyUncheckedEnumList) -> (
            r1: MyUncheckedEnumList,
            r2: MyUncheckedEnumList,
        );
        opMyUncheckedEnumCustomSeq(p1: MyUncheckedEnumCustomSeq, p2: MyUncheckedEnumCustomSeq) -> (
            r1: MyUncheckedEnumCustomSeq,
            r2: MyUncheckedEnumCustomSeq,
        );

        opMyStructList(p1: MyStructList, p2: MyStructList) -> (r1: MyStructList, r2: MyStructList);
        opMyStructCustomSeq(p1: MyStructCustomSeq, p2: MyStructCustomSeq) -> (
            r1: MyStructCustomSeq,
            r2: MyStructCustomSeq,
        );

        opOperationsList(p1: OperationsList, p2: OperationsList) -> (r1: OperationsList, r2: OperationsList);
        opOperationsCustomSeq(p1: OperationsCustomSeq, p2: OperationsCustomSeq) -> (
            r1: OperationsCustomSeq,
            r2: OperationsCustomSeq,
        );

        opAnotherStructList(p1: AnotherStructList, p2: AnotherStructList) -> (
            r1: AnotherStructList,
            r2: AnotherStructList,
        );
        opAnotherStructCustomSeq(p1: AnotherStructCustomSeq, p2: AnotherStructCustomSeq) -> (
            r1: AnotherStructCustomSeq,
            r2: AnotherStructCustomSeq,
        );
    }
}
