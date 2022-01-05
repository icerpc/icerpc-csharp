// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    // For each tagged param, return 2 tagged return values.
    interface OperationTagDouble
    {
        opByte(p1: tag(1) byte?) -> (r1: tag(1) byte?, r2: tag(2) byte?);

        opBool(p1: tag(1) bool?) -> (r1: tag(1) bool?, r2: tag(2) bool?);

        opShort(p1: tag(1) short?) -> (r1: tag(1) short?, r2: tag(2) short?);

        opInt(p1: tag(1) int?) -> (r1: tag(1) int?, r2: tag(2) int?);

        opLong(p1: tag(1) long?) -> (r1: tag(1) long?, r2: tag(2) long?);

        opFloat(p1: tag(1) float?) -> (r1: tag(1) float?, r2: tag(2) float?);

        opDouble(p1: tag(1) double?) -> (r1: tag(1) double?, r2: tag(2) double?);

        opString(p1: tag(1) string?) -> (r1: tag(1) string?, r2: tag(2) string?);

        opMyEnum(p1: tag(1) MyEnum?) -> (r1: tag(1) MyEnum?, r2: tag(2) MyEnum?);

        opMyStruct(p1: tag(1) MyStruct?) -> (r1: tag(1) MyStruct?, r2: tag(2) MyStruct?);

        opAnotherStruct(p1: tag(1) AnotherStruct?) -> (r1: tag(1) AnotherStruct?, r2: tag(2) AnotherStruct?);

        opByteSeq(p1: tag(1) ByteSeq?) -> (r1: tag(1) ByteSeq?, r2: tag(2) ByteSeq?);
        opByteList(p1: tag(1) ByteList?) -> (r1: tag(1) ByteList?, r2: tag(2) ByteList?);
        opOptionalByteSeq(p1: tag(1) OptionalByteSeq?) -> (r1: tag(1) OptionalByteSeq?, r2: tag(2) OptionalByteSeq?);

        opBoolSeq(p1: tag(1) BoolSeq?) -> (r1: tag(1) BoolSeq?, r2: tag(2) BoolSeq?);
        opBoolList(p1: tag(1) BoolList?) -> (r1: tag(1) BoolList?, r2: tag(2) BoolList?);
        opOptionalBoolSeq(p1: tag(1) OptionalBoolSeq?) -> (r1: tag(1) OptionalBoolSeq?, r2: tag(2) OptionalBoolSeq?);

        opShortSeq(p1: tag(1) ShortSeq?) -> (r1: tag(1) ShortSeq?, r2: tag(2) ShortSeq?);
        opShortList(p1: tag(1) ShortList?) -> (r1: tag(1) ShortList?, r2: tag(2) ShortList?);
        opOptionalShortSeq(p1: tag(1) OptionalShortSeq?) -> (
            r1: tag(1) OptionalShortSeq?,
            r2: tag(2) OptionalShortSeq?,
        );

        opIntSeq(p1: tag(1) IntSeq?) -> (r1: tag(1) IntSeq?, r2: tag(2) IntSeq?);
        opIntList(p1: tag(1) IntList?) -> (r1: tag(1) IntList?, r2: tag(2) IntList?);
        opOptionalIntSeq(p1: tag(1) OptionalIntSeq?) -> (r1: tag(1) OptionalIntSeq?, r2: tag(2) OptionalIntSeq?);

        opLongSeq(p1: tag(1) LongSeq?) -> (r1: tag(1) LongSeq?, r2: tag(2) LongSeq?);
        opLongList(p1: tag(1) LongList?) -> (r1: tag(1) LongList?, r2: tag(2) LongList?);
        opOptionalLongSeq(p1: tag(1) OptionalLongSeq?) -> (r1: tag(1) OptionalLongSeq?, r2: tag(2) OptionalLongSeq?);

        opFloatSeq(p1: tag(1) FloatSeq?) -> (r1: tag(1) FloatSeq?, r2: tag(2) FloatSeq?);
        opFloatList(p1: tag(1) FloatList?) -> (r1: tag(1) FloatList?, r2: tag(2) FloatList?);
        opOptionalFloatSeq(p1: tag(1) OptionalFloatSeq?) -> (
            r1: tag(1) OptionalFloatSeq?,
            r2: tag(2) OptionalFloatSeq?,
        );

        opDoubleSeq(p1: tag(1) DoubleSeq?) -> (r1: tag(1) DoubleSeq?, r2: tag(2) DoubleSeq?);
        opDoubleList(p1: tag(1) DoubleList?) -> (r1: tag(1) DoubleList?, r2: tag(2) DoubleList?);
        opOptionalDoubleSeq(p1: tag(1) OptionalDoubleSeq?) -> (
            r1: tag(1) OptionalDoubleSeq?,
            r2: tag(2) OptionalDoubleSeq?,
        );

        opStringSeq(p1: tag(1) StringSeq?) -> (r1: tag(1) StringSeq?, r2: tag(2) StringSeq?);
        opStringList(p1: tag(1) StringList?) -> (r1: tag(1) StringList?, r2: tag(2) StringList?);
        opOptionalStringSeq(p1: tag(1) OptionalStringSeq?) -> (
            r1: tag(1) OptionalStringSeq?,
            r2: tag(2) OptionalStringSeq?,
        );

        opMyEnumSeq(p1: tag(1) MyEnumSeq?) -> (r1: tag(1) MyEnumSeq?, r2: tag(2) MyEnumSeq?);
        opMyEnumList(p1: tag(1) MyEnumList?) -> (r1: tag(1) MyEnumList?, r2: tag(2) MyEnumList?);
        opOptionalMyEnumSeq(p1: tag(1) OptionalMyEnumSeq?) -> (
            r1: tag(1) OptionalMyEnumSeq?,
            r2: tag(2) OptionalMyEnumSeq?,
        );

        opMyFixedLengthEnumSeq(p1: tag(1) MyFixedLengthEnumSeq?) -> (
            r1: tag(1) MyFixedLengthEnumSeq?,
            r2: tag(2) MyFixedLengthEnumSeq?,
        );
        opMyFixedLengthEnumList(p1: tag(1) MyFixedLengthEnumList?) -> (
            r1: tag(1) MyFixedLengthEnumList?,
            r2: tag(2) MyFixedLengthEnumList?,
        );
        opOptionalMyFixedLengthEnumSeq(p1: tag(1) OptionalMyFixedLengthEnumSeq?) -> (
            r1: tag(1) OptionalMyFixedLengthEnumSeq?,
            r2: tag(2) OptionalMyFixedLengthEnumSeq?,
        );

        opMyStructSeq(p1: tag(1) MyStructSeq?) -> (r1: tag(1) MyStructSeq?, r2: tag(2) MyStructSeq?);
        opMyStructList(p1: tag(1) MyStructList?) -> (r1: tag(1) MyStructList?, r2: tag(2) MyStructList?);
        opOptionalMyStructSeq(p1: tag(1) OptionalMyStructSeq?) -> (
            r1: tag(1) OptionalMyStructSeq?,
            r2: tag(2) OptionalMyStructSeq?,
        );

        opAnotherStructSeq(p1: tag(1) AnotherStructSeq?) -> (
            r1: tag(1) AnotherStructSeq?,
            r2: tag(2) AnotherStructSeq?,
        );
        opAnotherStructList(p1: tag(1) AnotherStructList?) -> (
            r1: tag(1) AnotherStructList?,
            r2: tag(2) AnotherStructList?,
        );
        opOptionalAnotherStructSeq(p1: tag(1) OptionalAnotherStructSeq?) -> (
            r1: tag(1) OptionalAnotherStructSeq?,
            r2: tag(2) OptionalAnotherStructSeq?,
        );

        opIntDict(p1: tag(1) IntDict?) -> (r1: tag(1) IntDict?, r2: tag(2) IntDict?);
        opStringDict(p1: tag(1) StringDict?) -> (r1: tag(1) StringDict?, r2: tag(2) StringDict?);

        opIntSortedDict(p1: tag(1) IntSortedDict?) -> (r1: tag(1) IntSortedDict?, r2: tag(2) IntSortedDict?);
        opStringSortedDict(p1: tag(1) StringSortedDict?) -> (
            r1: tag(1) StringSortedDict?,
            r2: tag(2) StringSortedDict?,
        );

        opOptionalIntSortedDict(p1: tag(1) OptionalIntSortedDict?) -> (
            r1: tag(1) OptionalIntSortedDict?,
            r2: tag(2) OptionalIntSortedDict?,
        );
        opOptionalStringSortedDict(p1: tag(1) OptionalStringSortedDict?) -> (
            r1: tag(1) OptionalStringSortedDict?,
            r2: tag(2) OptionalStringSortedDict?,
        );

        opOptionalIntDict(p1: tag(1) OptionalIntDict?) -> (r1: tag(1) OptionalIntDict?, r2: tag(2) OptionalIntDict?);
        opOptionalStringDict(p1: tag(1) OptionalStringDict?) -> (
            r1: tag(1) OptionalStringDict?,
            r2: tag(2) OptionalStringDict?,
        );
    }

    interface OperationTagEncodedResult
    {
        [cs:encoded-result] opMyStruct(p1: tag(1) MyStruct?) -> tag(1) MyStruct?;
        [cs:encoded-result] opStringSeq(p1: tag(1) StringSeq?) -> tag(1) StringSeq?;
        [cs:encoded-result] opIntDict(p1: tag(1) IntDict?) -> tag(1) IntDict?;
    }

    interface OperationTag
    {
        opInt(p1: tag(0) int?) -> tag(1) int?;

        opVoid();
    }

    // An interface compatible with OperationTag except with fewer tags.
    interface OperationTagMinus
    {
        opInt() -> tag(1) int?;
    }

    // An interface compatible with OperationTag except with more tags.
    interface OperationTagPlus
    {
        opInt(p1: tag(0) int?, p2: tag(1) string?) -> (r1: tag(1) int?, r2: tag(2) string?);

        opVoid(p1: tag(1) string?) -> tag(1) string?;
    }
}
