// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    class OneTagged
    {
        a: tag(1) int?,
    }

    class MultiTagged
    {
        mByte: tag(1) byte?,
        mBool: tag(2) bool?,
        mShort: tag(3) short?,
        mInt: tag(4) int?,
        mLong: tag(5) long?,
        mFloat: tag(6) float?,
        mDouble: tag(7) double?,
        mUShort: tag(8) ushort?,
        mUInt: tag(9) uint?,
        mULong: tag(10) ulong?,
        mVarInt: tag(11) varint?,
        mVarLong: tag(12) varlong?,
        mVarUInt: tag(13) varuint?,
        mVarULong: tag(14) varulong?,
        mString: tag(15) string?,

        mMyEnum: tag(20) MyEnum?,
        mMyStruct: tag(21) MyStruct?,
        mAnotherStruct: tag(22) AnotherStruct?,

        mByteSeq: tag(30) ByteSeq?,
        mStringSeq: tag(31) StringSeq?,
        mShortSeq: tag(32) ShortSeq?,
        mMyEnumSeq: tag(33) MyEnumSeq?,
        mMyStructSeq: tag(34) MyStructSeq?,
        mAnotherStructSeq: tag(35) AnotherStructSeq?,

        mIntDict: tag(40) IntDict?,
        mStringDict: tag(41) StringDict?,
        mUShortSeq: tag(42) UShortSeq?,
        mVarULongSeq: tag(43) VarULongSeq?,
        mVarIntSeq: tag(44) VarIntSeq?,

        mByteDict: tag(50) ByteDict?,
        mMyStructDict: tag(51) MyStructDict?,
        mAnotherStructDict: tag(52) AnotherStructDict?,
    }

    class A
    {
        mInt1: int,
        mInt2: tag(1) int?,
        mInt3: tag(50) int?,
        mInt4: tag(500) int?,
    }

    [preserve-slice]
    class B : A
    {
        mInt5: int,
        mInt6: tag(10) int?,
    }

    class C : B
    {
        mString1: string,
        mString2: tag(890) string?,
    }

    class TaggedWithCustom
    {
        mMyStructList: tag(1) MyStructList?,
        mAnotherStructList: tag(2) AnotherStructList?,
    }

    interface ClassTag
    {
        pingPong(o: AnyClass) -> AnyClass;
    }
}
