// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    // For each tagged param, return 2 tagged return values.
    interface OperationTagDouble
    {
        (tag(1) byte? r1, tag(2) byte? r2) opByte(tag(1) byte? p1);

        (tag(1) bool? r1, tag(2) bool? r2) opBool(tag(1) bool? p1);

        (tag(1) short? r1, tag(2) short? r2) opShort(tag(1) short? p1);

        (tag(1) int? r1, tag(2) int? r2) opInt(tag(1) int? p1);

        (tag(1) long? r1, tag(2) long? r2) opLong(tag(1) long? p1);

        (tag(1) float? r1, tag(2) float? r2) opFloat(tag(1) float? p1);

        (tag(1) double? r1, tag(2) double? r2) opDouble(tag(1) double? p1);

        (tag(1) string? r1, tag(2) string? r2) opString(tag(1) string? p1);

        (tag(1) MyEnum? r1, tag(2) MyEnum? r2) opMyEnum(tag(1) MyEnum? p1);

        (tag(1) MyStruct? r1, tag(2) MyStruct? r2) opMyStruct(tag(1) MyStruct? p1);

        (tag(1) AnotherStruct? r1, tag(2) AnotherStruct? r2) opAnotherStruct(tag(1) AnotherStruct? p1);

        (tag(1) ByteSeq? r1, tag(2) ByteSeq? r2) opByteSeq(tag(1) ByteSeq? p1);
        (tag(1) ByteList? r1, tag(2) ByteList? r2) opByteList(tag(1) ByteList? p1);
        (tag(1) OptionalByteSeq? r1, tag(2) OptionalByteSeq? r2) opOptionalByteSeq(
            tag(1) OptionalByteSeq? p1);

        (tag(1) BoolSeq? r1, tag(2) BoolSeq? r2) opBoolSeq(tag(1) BoolSeq? p1);
        (tag(1) BoolList? r1, tag(2) BoolList? r2) opBoolList(tag(1) BoolList? p1);
        (tag(1) OptionalBoolSeq? r1, tag(2) OptionalBoolSeq? r2) opOptionalBoolSeq(
            tag(1) OptionalBoolSeq? p1);

        (tag(1) ShortSeq? r1, tag(2) ShortSeq? r2) opShortSeq(tag(1) ShortSeq? p1);
        (tag(1) ShortList? r1, tag(2) ShortList? r2) opShortList(tag(1) ShortList? p1);
        (tag(1) OptionalShortSeq? r1, tag(2) OptionalShortSeq? r2) opOptionalShortSeq(
            tag(1) OptionalShortSeq? p1);

        (tag(1) IntSeq? r1, tag(2) IntSeq? r2) opIntSeq(tag(1) IntSeq? p1);
        (tag(1) IntList? r1, tag(2) IntList? r2) opIntList(tag(1) IntList? p1);
        (tag(1) OptionalIntSeq? r1, tag(2) OptionalIntSeq? r2) opOptionalIntSeq(tag(1) OptionalIntSeq? p1);

        (tag(1) LongSeq? r1, tag(2) LongSeq? r2) opLongSeq(tag(1) LongSeq? p1);
        (tag(1) LongList? r1, tag(2) LongList? r2) opLongList(tag(1) LongList? p1);
        (tag(1) OptionalLongSeq? r1, tag(2) OptionalLongSeq? r2) opOptionalLongSeq(tag(1) OptionalLongSeq? p1);

        (tag(1) FloatSeq? r1, tag(2) FloatSeq? r2) opFloatSeq(tag(1) FloatSeq? p1);
        (tag(1) FloatList? r1, tag(2) FloatList? r2) opFloatList(tag(1) FloatList? p1);
        (tag(1) OptionalFloatSeq? r1, tag(2) OptionalFloatSeq? r2) opOptionalFloatSeq(tag(1) OptionalFloatSeq? p1);

        (tag(1) DoubleSeq? r1, tag(2) DoubleSeq? r2) opDoubleSeq(tag(1) DoubleSeq? p1);
        (tag(1) DoubleList? r1, tag(2) DoubleList? r2) opDoubleList(tag(1) DoubleList? p1);
        (tag(1) OptionalDoubleSeq? r1, tag(2) OptionalDoubleSeq? r2) opOptionalDoubleSeq(tag(1) OptionalDoubleSeq? p1);

        (tag(1) StringSeq? r1, tag(2) StringSeq? r2) opStringSeq(tag(1) StringSeq? p1);
        (tag(1) StringList? r1, tag(2) StringList? r2) opStringList(tag(1) StringList? p1);
        (tag(1) OptionalStringSeq? r1, tag(2) OptionalStringSeq? r2) opOptionalStringSeq(
            tag(1) OptionalStringSeq? p1);

        (tag(1) MyEnumSeq? r1, tag(2) MyEnumSeq? r2) opMyEnumSeq(tag(1) MyEnumSeq? p1);
        (tag(1) MyEnumList? r1, tag(2) MyEnumList? r2) opMyEnumList(tag(1) MyEnumList? p1);
        (tag(1) OptionalMyEnumSeq? r1, tag(2) OptionalMyEnumSeq? r2) opOptionalMyEnumSeq(
            tag(1) OptionalMyEnumSeq? p1);

        (tag(1) MyFixedLengthEnumSeq? r1, tag(2) MyFixedLengthEnumSeq? r2) opMyFixedLengthEnumSeq(
            tag(1) MyFixedLengthEnumSeq? p1);
        (tag(1) MyFixedLengthEnumList? r1, tag(2) MyFixedLengthEnumList? r2) opMyFixedLengthEnumList(
            tag(1) MyFixedLengthEnumList? p1);
        (tag(1) OptionalMyFixedLengthEnumSeq? r1, tag(2) OptionalMyFixedLengthEnumSeq? r2) opOptionalMyFixedLengthEnumSeq(
            tag(1) OptionalMyFixedLengthEnumSeq? p1);

        (tag(1) MyStructSeq? r1, tag(2) MyStructSeq? r2) opMyStructSeq(tag(1) MyStructSeq? p1);
        (tag(1) MyStructList? r1, tag(2) MyStructList? r2) opMyStructList(tag(1) MyStructList? p1);
        (tag(1) OptionalMyStructSeq? r1, tag(2) OptionalMyStructSeq? r2) opOptionalMyStructSeq(
            tag(1) OptionalMyStructSeq? p1);

        (tag(1) AnotherStructSeq? r1, tag(2) AnotherStructSeq? r2) opAnotherStructSeq(tag(1) AnotherStructSeq? p1);
        (tag(1) AnotherStructList? r1, tag(2) AnotherStructList? r2) opAnotherStructList(tag(1) AnotherStructList? p1);
        (tag(1) OptionalAnotherStructSeq? r1, tag(2) OptionalAnotherStructSeq? r2) opOptionalAnotherStructSeq(
            tag(1) OptionalAnotherStructSeq? p1);

        (tag(1) IntDict? r1, tag(2) IntDict? r2) opIntDict(tag(1) IntDict? p1);
        (tag(1) StringDict? r1, tag(2) StringDict? r2) opStringDict(tag(1) StringDict? p1);

        (tag(1) IntSortedDict? r1, tag(2) IntSortedDict? r2) opIntSortedDict(tag(1) IntSortedDict? p1);
        (tag(1) StringSortedDict? r1, tag(2) StringSortedDict? r2) opStringSortedDict(tag(1) StringSortedDict? p1);

        (tag(1) OptionalIntSortedDict? r1, tag(2) OptionalIntSortedDict? r2) opOptionalIntSortedDict(
            tag(1) OptionalIntSortedDict? p1);
        (tag(1) OptionalStringSortedDict? r1, tag(2) OptionalStringSortedDict? r2) opOptionalStringSortedDict(
            tag(1) OptionalStringSortedDict? p1);

        (tag(1) OptionalIntDict? r1, tag(2) OptionalIntDict? r2) opOptionalIntDict(tag(1) OptionalIntDict? p1);
        (tag(1) OptionalStringDict? r1, tag(2) OptionalStringDict? r2) opOptionalStringDict(tag(1) OptionalStringDict? p1);
    }

    interface OperationTagMarshaledResult
    {
        [marshaled-result] tag(1) MyStruct? opMyStructMarshaledResult(tag(1) MyStruct? p1);
        [marshaled-result] tag(1) StringSeq? opStringSeqMarshaledResult(tag(1) StringSeq? p1);
        [marshaled-result] tag(1) IntDict? opIntDictMarshaledResult(tag(1) IntDict? p1);
    }

    interface OperationTag
    {
        tag(1) int? opInt(tag(0) int? p1);

        void opVoid();
    }

    // An interface compatible with OperationTag except with fewer tags.
    interface OperationTagMinus
    {
        tag(1) int? opInt();
    }

    // An interface compatible with OperationTag except with more tags.
    interface OperationTagPlus
    {
        (tag(1) int? r1, tag(2) string? r2) opInt(tag(0) int? p1, tag(1) string? p2);

        tag(1) string? opVoid(tag(1) string? p1);
    }
}
