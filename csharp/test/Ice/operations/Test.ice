//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[3.7]]
[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Context.ice>

module IceRpc::Test::Operations
{

enum MyEnum
{
    enum1,
    enum2,
    enum3
}

interface MyClass;

struct AnotherStruct
{
    string s;
}

struct Structure
{
    MyClass* p;
    MyEnum e;
    AnotherStruct s;
}

sequence<byte> ByteS;
sequence<bool> BoolS;
sequence<short> ShortS;
sequence<int> IntS;
sequence<varint> VarIntS;
sequence<long> LongS;
sequence<varlong> VarLongS;
sequence<ushort> UShortS;
sequence<uint> UIntS;
sequence<varuint> VarUIntS;
sequence<ulong> ULongS;
sequence<varulong> VarULongS;
sequence<float> FloatS;
sequence<double> DoubleS;
sequence<string> StringS;
sequence<MyEnum> MyEnumS;
sequence<MyClass*> MyClassS;

sequence<ByteS> ByteSS;
sequence<BoolS> BoolSS;
sequence<ShortS> ShortSS;
sequence<IntS> IntSS;
sequence<LongS> LongSS;
sequence<VarLongS> VarLongSS;
sequence<UShortS> UShortSS;
sequence<UIntS> UIntSS;
sequence<ULongS> ULongSS;
sequence<VarULongS> VarULongSS;
sequence<FloatS> FloatSS;
sequence<DoubleS> DoubleSS;
sequence<StringS> StringSS;
sequence<MyEnumS> MyEnumSS;
sequence<MyClassS> MyClassSS;

sequence<StringSS> StringSSS;

struct MyStruct
{
    int i;
    int j;
}

dictionary<byte, bool> ByteBoolD;
dictionary<short, int> ShortIntD;
dictionary<long, float> LongFloatD;
dictionary<ushort, uint> UShortUIntD;
dictionary<ulong, float> ULongFloatD;
dictionary<string, string> StringStringD;
dictionary<string, MyEnum> StringMyEnumD;
dictionary<MyEnum, string> MyEnumStringD;
dictionary<MyStruct, MyEnum> MyStructMyEnumD;

sequence<ByteBoolD> ByteBoolDS;
sequence<ShortIntD> ShortIntDS;
sequence<UShortUIntD> UShortUIntDS;
sequence<LongFloatD> LongFloatDS;
sequence<ULongFloatD> ULongFloatDS;
sequence<StringStringD> StringStringDS;
sequence<StringMyEnumD> StringMyEnumDS;
sequence<MyEnumStringD> MyEnumStringDS;
sequence<MyStructMyEnumD> MyStructMyEnumDS;

dictionary<byte, ByteS> ByteByteSD;
dictionary<bool, BoolS> BoolBoolSD;
dictionary<short, ShortS> ShortShortSD;
dictionary<int, IntS> IntIntSD;
dictionary<long, LongS> LongLongSD;
dictionary<ushort, UShortS> UShortUShortSD;
dictionary<uint, UIntS> UIntUIntSD;
dictionary<ulong, ULongS> ULongULongSD;
dictionary<string, FloatS> StringFloatSD;
dictionary<string, DoubleS> StringDoubleSD;
dictionary<string, StringS> StringStringSD;
dictionary<MyEnum, MyEnumS> MyEnumMyEnumSD;

exception SomeException {}

interface MyClass
{
    void shutdown();

    bool supportsCompress();

    void opVoid();

    (byte r1, byte r2) opByte(byte p1, byte p2);

    (bool r1, bool r2) opBool(bool p1, bool p2);

    (long r1, short r2, int r3, long r4) opShortIntLong(short p1, int p2, long p3);
    (ulong r1, ushort r2, uint r3, ulong r4) opUShortUIntULong(ushort p1, uint p2, ulong p3);

    varint opVarInt(varint v);
    varuint opVarUInt(varuint v);

    varlong opVarLong(varlong v);
    varulong opVarULong(varulong v);

    (double r1, float r2, double r3) opFloatDouble(float p1, double p2);

    (string r1, string r2) opString(string p1, string p2);

    (MyEnum r1, MyEnum r2) opMyEnum(MyEnum p1);

    (MyClass? r1, MyClass? r2, MyClass? r3) opMyClass(MyClass? p1);

    (Structure r1, Structure r2) opStruct(Structure p1, Structure p2);

    (ByteS r1, ByteS r2) opByteS(ByteS p1, ByteS p2);

    (BoolS r1, BoolS r2) opBoolS(BoolS p1, BoolS p2);

    (LongS r1, ShortS r2, IntS r3, LongS r4) opShortIntLongS(ShortS p1, IntS p2, LongS p3);
    (ULongS r1, UShortS r2, UIntS r3, ULongS r4) opUShortUIntULongS(UShortS p1, UIntS p2, ULongS p3);

    (VarLongS r1, VarIntS r2, VarLongS r3) opVarIntVarLongS(VarIntS p1, VarLongS p2);
    (VarULongS r1, VarUIntS r2, VarULongS r3) opVarUIntVarULongS(VarUIntS p1, VarULongS p2);

    (DoubleS r1, FloatS r2, DoubleS r3) opFloatDoubleS(FloatS p1, DoubleS p2);

    (StringS r1, StringS r2) opStringS(StringS p1, StringS p2);

    (ByteSS r1, ByteSS r2) opByteSS(ByteSS p1, ByteSS p2);

    (BoolSS r1, BoolSS r2) opBoolSS(BoolSS p1, BoolSS p2);

    (LongSS r1, ShortSS r2, IntSS r3, LongSS r4) opShortIntLongSS(ShortSS p1, IntSS p2, LongSS p3);
    (ULongSS r1, UShortSS r2, UIntSS r3, ULongSS r4) opUShortUIntULongSS(UShortSS p1, UIntSS p2, ULongSS p3);

    (DoubleSS r1, FloatSS r2, DoubleSS r3) opFloatDoubleSS(FloatSS p1, DoubleSS p2);

    (StringSS r1, StringSS r2) opStringSS(StringSS p1, StringSS p2);

    (StringSSS r1, StringSSS r2) opStringSSS(StringSSS p1, StringSSS p2);

    (ByteBoolD r1, ByteBoolD r2) opByteBoolD(ByteBoolD p1, ByteBoolD p2);

    (ShortIntD r1, ShortIntD r2) opShortIntD(ShortIntD p1, ShortIntD p2);
    (UShortUIntD r1, UShortUIntD r2) opUShortUIntD(UShortUIntD p1, UShortUIntD p2);

    (LongFloatD r1, LongFloatD r2) opLongFloatD(LongFloatD p1, LongFloatD p2);
    (ULongFloatD r1, ULongFloatD r2) opULongFloatD(ULongFloatD p1, ULongFloatD p2);

    (StringStringD r1, StringStringD r2) opStringStringD(StringStringD p1, StringStringD p2);

    (StringMyEnumD r1, StringMyEnumD r2) opStringMyEnumD(StringMyEnumD p1, StringMyEnumD p2);

    (MyEnumStringD r1, MyEnumStringD r2) opMyEnumStringD(MyEnumStringD p1, MyEnumStringD p2);

    (MyStructMyEnumD r1, MyStructMyEnumD r2) opMyStructMyEnumD(MyStructMyEnumD p1, MyStructMyEnumD p2);

    (ByteBoolDS r1, ByteBoolDS r2) opByteBoolDS(ByteBoolDS p1, ByteBoolDS p2);

    (ShortIntDS r1, ShortIntDS r2) opShortIntDS(ShortIntDS p1, ShortIntDS p2);
    (UShortUIntDS r1, UShortUIntDS r2) opUShortUIntDS(UShortUIntDS p1, UShortUIntDS p2);

    (LongFloatDS r1, LongFloatDS r2) opLongFloatDS(LongFloatDS p1, LongFloatDS p2);
    (ULongFloatDS r1, ULongFloatDS r2) opULongFloatDS(ULongFloatDS p1, ULongFloatDS p2);

    (StringStringDS r1, StringStringDS r2) opStringStringDS(StringStringDS p1, StringStringDS p2);

    (StringMyEnumDS r1, StringMyEnumDS r2) opStringMyEnumDS(StringMyEnumDS p1, StringMyEnumDS p2);

    (MyEnumStringDS r1, MyEnumStringDS r2) opMyEnumStringDS(MyEnumStringDS p1, MyEnumStringDS p2);

    (MyStructMyEnumDS r1, MyStructMyEnumDS r2) opMyStructMyEnumDS(MyStructMyEnumDS p1, MyStructMyEnumDS p2);

    (ByteByteSD r1, ByteByteSD r2) opByteByteSD(ByteByteSD p1, ByteByteSD p2);

    (BoolBoolSD r1, BoolBoolSD r2) opBoolBoolSD(BoolBoolSD p1, BoolBoolSD p2);

    (ShortShortSD r1, ShortShortSD r2) opShortShortSD(ShortShortSD p1, ShortShortSD p2);
    (UShortUShortSD r1, UShortUShortSD r2) opUShortUShortSD(UShortUShortSD p1, UShortUShortSD p2);

    (IntIntSD r1, IntIntSD r2) opIntIntSD(IntIntSD p1, IntIntSD p2);
    (UIntUIntSD r1, UIntUIntSD r2) opUIntUIntSD(UIntUIntSD p1, UIntUIntSD p2);

    (LongLongSD r1, LongLongSD r2) opLongLongSD(LongLongSD p1, LongLongSD p2);
    (ULongULongSD r1, ULongULongSD r2) opULongULongSD(ULongULongSD p1, ULongULongSD p2);

    (StringFloatSD r1, StringFloatSD r2) opStringFloatSD(StringFloatSD p1, StringFloatSD p2);

    (StringDoubleSD r1, StringDoubleSD r2) opStringDoubleSD(StringDoubleSD p1, StringDoubleSD p2);

    (StringStringSD r1, StringStringSD r2) opStringStringSD(StringStringSD p1, StringStringSD p2);

    (MyEnumMyEnumSD r1, MyEnumMyEnumSD r2) opMyEnumMyEnumSD(MyEnumMyEnumSD p1, MyEnumMyEnumSD p2);

    IntS opIntS(IntS s);

    void opByteSOneway(ByteS s);

    int opByteSOnewayCallCount();

    IceRpc::Context opContext();

    void opDoubleMarshaling(double p1, DoubleS p2);

    idempotent void opIdempotent();

    void opOneway();

    [oneway]
    void opOnewayMetadata();

    byte opByte1(byte opByte1);
    short opShort1(short opShort1);
    int opInt1(int opInt1);
    long opLong1(long opLong1);
    ushort opUShort1(ushort opUShort1);
    uint opUInt1(uint opUInt1);
    ulong opULong1(ulong opULong1);
    float opFloat1(float opFloat1);
    double opDouble1(double opDouble1);
    string opString1(string opString1);
    StringS opStringS1(StringS opStringS1);
    ByteBoolD opByteBoolD1(ByteBoolD opByteBoolD1);
    StringS opStringS2(StringS stringS);
    ByteBoolD opByteBoolD2(ByteBoolD byteBoolD);

    [marshaled-result] Structure opMStruct1();
    [marshaled-result] (Structure r1, Structure r2) opMStruct2(Structure p1);

    [marshaled-result] StringS opMSeq1();
    [marshaled-result] (StringS r1, StringS r2) opMSeq2(StringS p1);

    [marshaled-result] StringStringD opMDict1();
    [marshaled-result] (StringStringD r1, StringStringD r2) opMDict2(StringStringD p1);

    void opSendStream1(stream byte p1);
    void opSendStream2(int p1, stream byte p2);

    stream byte opGetStream1();
    (int r1, stream byte r2) opGetStream2();

    stream byte opSendAndGetStream1(stream byte p1);
    (int r1, stream byte r2) opSendAndGetStream2(int p1, stream byte p2);
}

struct MyStruct1
{
    string tesT; // Same name as the enclosing module
    MyClass* myClass; // Same name as an already defined class
}

class MyClass1
{
    string tesT; // Same name as the enclosing module
    MyClass* myClass; // Same name as an already defined class
}

interface MyDerivedClass : MyClass
{
    void opDerived();
    MyClass1 opMyClass1(MyClass1 opMyClass1);
    MyStruct1 opMyStruct1(MyStruct1 opMyStruct1);
}

}
