// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    sequence<byte> ByteSeq;
    sequence<bool> BoolSeq;
    sequence<short> ShortSeq;
    sequence<ushort> UShortSeq;
    sequence<int> IntSeq;
    sequence<varint> VarIntSeq;
    sequence<uint> UIntSeq;
    sequence<varuint> VarUIntSeq;
    sequence<long> LongSeq;
    sequence<varlong> VarLongSeq;
    sequence<ulong> ULongSeq;
    sequence<varulong> VarULongSeq;
    sequence<float> FloatSeq;
    sequence<double> DoubleSeq;
    sequence<string> StringSeq;

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

    enum MyEnum
    {
        enum1,
        enum2,
        enum3
    }

    struct MyStruct
    {
        int i;
        int j;
    }

    interface InvocationService;

    struct AnotherStruct
    {
        string str;
        InvocationService prx;
        MyEnum en;
        MyStruct st;
    }

    sequence<MyEnum> MyEnumSeq;
    sequence<MyStruct> MyStructSeq;
    sequence<InvocationService> InvocationServiceSeq;
    sequence<AnotherStruct> AnotherStructSeq;

    dictionary<string, MyEnum> MyEnumDict;
    dictionary<string, MyStruct> MyStructDict;
    dictionary<string, InvocationService> InvocationServiceDict;
    dictionary<string, AnotherStruct> AnotherStructDict;

    exception SomeException {}

    interface InvocationService
    {
        // Builtin types
        (byte r1, byte r2) opByte(byte p1, byte p2);
        (bool r1, bool r2) opBool(bool p1, bool p2);
        (short r1, short r2) opShort(short p1, short p2);
        (ushort r1, ushort r2) opUShort(ushort p1, ushort p2);
        (int r1, int r2) opInt(int p1, int p2);
        (varint r1, varint r2) opVarInt(varint p1, varint p2);
        (uint r1, uint r2) opUInt(uint p1, uint p2);
        (varuint r1, varuint r2) opVarUInt(varuint p1, varuint p2);
        (long r1, long r2) opLong(long p1, long p2);
        (varlong r1, varlong r2) opVarLong(varlong p1, varlong p2);
        (ulong r1, ulong r2) opULong(ulong p1, ulong p2);
        (varulong r1, varulong r2) opVarULong(varulong p1, varulong p2);
        (float r1, float r2) opFloat(float p1, float p2);
        (double r1, double r2) opDouble(double p1, double p2);
        (string r1, string r2) opString(string p1, string p2);

        // Builtin type sequences
        (ByteSeq r1, ByteSeq r2) opByteSeq(ByteSeq p1, ByteSeq p2);
        (BoolSeq r1, BoolSeq r2) opBoolSeq(BoolSeq p1, BoolSeq p2);
        (ShortSeq r1, ShortSeq r2) opShortSeq(ShortSeq p1, ShortSeq p2);
        (UShortSeq r1, UShortSeq r2) opUShortSeq(UShortSeq p1, UShortSeq p2);
        (IntSeq r1, IntSeq r2) opIntSeq(IntSeq p1, IntSeq p2);
        (VarIntSeq r1, VarIntSeq r2) opVarIntSeq(VarIntSeq p1, VarIntSeq p2);
        (UIntSeq r1, UIntSeq r2) opUIntSeq(UIntSeq p1, UIntSeq p2);
        (VarUIntSeq r1, VarUIntSeq r2) opVarUIntSeq(VarUIntSeq p1, VarUIntSeq p2);
        (LongSeq r1, LongSeq r2) opLongSeq(LongSeq p1, LongSeq p2);
        (VarLongSeq r1, VarLongSeq r2) opVarLongSeq(VarLongSeq p1, VarLongSeq p2);
        (ULongSeq r1, ULongSeq r2) opULongSeq(ULongSeq p1, ULongSeq p2);
        (VarULongSeq r1, VarULongSeq r2) opVarULongSeq(VarULongSeq p1, VarULongSeq p2);
        (FloatSeq r1, FloatSeq r2) opFloatSeq(FloatSeq p1, FloatSeq p2);
        (DoubleSeq r1, DoubleSeq r2) opDoubleSeq(DoubleSeq p1, DoubleSeq p2);
        (StringSeq r1, StringSeq r2) opStringSeq(StringSeq p1, StringSeq p2);

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

        // Defined types
        (MyEnum r1, MyEnum r2) opMyEnum(MyEnum p1, MyEnum p2);
        (MyStruct r1, MyStruct r2) opMyStruct(MyStruct p1, MyStruct p2);
        (AnotherStruct r1, AnotherStruct r2) opAnotherStruct(AnotherStruct p1, AnotherStruct p2);

        // Defined type sequences
        (MyEnumSeq r1, MyEnumSeq r2) opMyEnumSeq(MyEnumSeq p1, MyEnumSeq p2);
        (MyStructSeq r1, MyStructSeq r2) opMyStructSeq(MyStructSeq p1, MyStructSeq p2);
        (AnotherStructSeq r1, AnotherStructSeq r2) opAnotherStructSeq(AnotherStructSeq p1, AnotherStructSeq p2);

        // Defined type dictionaries
        (MyEnumDict r1, MyEnumDict r2) opMyEnumDict(MyEnumDict p1, MyEnumDict p2);
        (MyStructDict r1, MyStructDict r2) opMyStructDict(MyStructDict p1, MyStructDict p2);
        (AnotherStructDict r1, AnotherStructDict r2) opAnotherStructDict(AnotherStructDict p1, AnotherStructDict p2);

        // Marshalled result
        [marshaled-result] AnotherStruct opAnotherStruct1(AnotherStruct p1);
        [marshaled-result] (AnotherStruct r1, AnotherStruct r2) opAnotherStruct2(AnotherStruct p1);

        [marshaled-result] StringSeq opStringSeq1(StringSeq p1);
        [marshaled-result] (StringSeq r1, StringSeq r2) opStringSeq2(StringSeq p1);

        [marshaled-result] StringDict opStringDict1(StringDict p1);
        [marshaled-result] (StringDict r1, StringDict r2) opStringDict2(StringDict p1);

        // Oneway Operations
        void opOneway();
        [oneway]
        void opOnewayMetadata();
    }

    interface DerivedInvocationService : InvocationService
    {
    }
}
