// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    struct OneOptional
    {
        a: int?,
    }

    struct MultiOptional
    {
        mByte: byte?,
        mBool: bool?,
        mShort: short?,
        mInt: int?,
        mLong: long?,
        mFloat: float?,
        mDouble: double?,
        mUShort: ushort?,
        mUInt: uint?,
        mULong: ulong?,
        mVarInt: varint?,
        mVarLong: varlong?,
        mVarUInt: varuint?,
        mVarULong: varulong?,
        mString: string?,

        mMyEnum: MyEnum?,
        mMyStruct: MyStruct?,
        mAnotherStruct: AnotherStruct?,

        mByteSeq: ByteSeq?,
        mStringSeq: StringSeq?,
        mShortSeq: ShortSeq?,
        mMyEnumSeq: MyEnumSeq?,
        mMyStructSeq: MyStructSeq?,
        mAnotherStructSeq: AnotherStructSeq?,

        mIntDict: IntDict?,
        mStringDict: StringDict?,
        mUShortSeq: UShortSeq?,
        mVarULongSeq: VarULongSeq?,
        mVarIntSeq: VarIntSeq?,

        mByteDict: ByteDict?,
        mMyStructDict: MyStructDict?,
        mAnotherStructDict: AnotherStructDict?,
    }

    interface OptionalOperations
    {
        pingPongOne(o: OneOptional?) -> OneOptional?;
        pingPongMulti(o: MultiOptional?) -> MultiOptional?;

        opByte(p1: byte?) -> (r1: byte?, r2: byte?);

        opBool(p1: bool?) -> (r1: bool?, r2: bool?);

        opShort(p1: short?) -> (r1: short?, r2: short?);

        opInt(p1: int?) -> (r1: int?, r2: int?);

        opLong(p1: long?) -> (r1: long?, r2: long?);

        opFloat(p1: float?) -> (r1: float?, r2: float?);

        opDouble(p1: double?) -> (r1: double?, r2: double?);

        opString(p1: string?) -> (r1: string?, r2: string?);

        opMyEnum(p1: MyEnum?) -> (r1: MyEnum?, r2: MyEnum?);

        opMyStruct(p1: MyStruct?) -> (r1: MyStruct?, r2: MyStruct?);

        opAnotherStruct(p1: AnotherStruct?) -> (r1: AnotherStruct?, r2: AnotherStruct?);

        opByteSeq(p1: ByteSeq?) -> (r1: ByteSeq?, r2: ByteSeq?);
        opByteList(p1: ByteList?) -> (r1: ByteList?, r2: ByteList?);

        opBoolSeq(p1: BoolSeq?) -> (r1: BoolSeq?, r2: BoolSeq?);
        opBoolList(p1: BoolList?) -> (r1: BoolList?, r2: BoolList?);

        opShortSeq(p1: ShortSeq?) -> (r1: ShortSeq?, r2: ShortSeq?);
        opShortList(p1: ShortList?) -> (r1: ShortList?, r2: ShortList?);

        opIntSeq(p1: IntSeq?) -> (r1: IntSeq?, r2: IntSeq?);
        opIntList(p1: IntList?) -> (r1: IntList?, r2: IntList?);

        opLongSeq(p1: LongSeq?) -> (r1: LongSeq?, r2: LongSeq?);
        opLongList(p1: LongList?) -> (r1: LongList?, r2: LongList?);

        opFloatSeq(p1: FloatSeq?) -> (r1: FloatSeq?, r2: FloatSeq?);
        opFloatList(p1: FloatList?) -> (r1: FloatList?, r2: FloatList?);

        opDoubleSeq(p1: DoubleSeq?) -> (r1: DoubleSeq?, r2: DoubleSeq?);
        opDoubleList(p1: DoubleList?) -> (r1: DoubleList?, r2: DoubleList?);

        opStringSeq(p1: StringSeq?) -> (r1: StringSeq?, r2: StringSeq?);
        opStringList(p1: StringList?) -> (r1: StringList?, r2: StringList?);

        opMyStructSeq(p1: MyStructSeq?) -> (r1: MyStructSeq?, r2: MyStructSeq?);
        opMyStructList(p1: MyStructList?) -> (r1: MyStructList?, r2: MyStructList?);

        opAnotherStructSeq(p1: AnotherStructSeq?) -> (r1: AnotherStructSeq?, r2: AnotherStructSeq?);
        opAnotherStructList(p1: AnotherStructList?) -> (r1: AnotherStructList?, r2: AnotherStructList?);

        opIntDict(p1: IntDict?) -> (r1: IntDict?, r2: IntDict?);
        opStringDict(p1: StringDict?) -> (r1: StringDict?, r2: StringDict?);

        [cs:encoded-result] opMyStructMarshaledResult(p1: MyStruct?) -> MyStruct?;
        [cs:encoded-result] opStringSeqMarshaledResult(p1: StringSeq?) -> StringSeq?;
        [cs:encoded-result] opIntDictMarshaledResult(p1: IntDict?) -> IntDict?;
    }
}
