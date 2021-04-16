// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <EnumTests.ice>
#include <StructTests.ice>
#include <ClassTests.ice>
#include <SequenceTests.ice>
#include <DictionaryTests.ice>

module IceRpc::Tests::CodeGeneration
{
    // builtin types
    using boolA = bool;
    using byteA = byte;
    using shortA = short;
    using ushortA = ushort;
    using intA = int;
    using uintA = uint;
    using varintA = varint;
    using varuintA = varuint;
    using longA = long;
    using ulongA = ulong;
    using varlongA = varlong;
    using varulongA = varulong;
    using floatA = float;
    using doubleA = double;
    using stringA = string;

    using MyEnumA = MyEnum;
    using MyStructA = MyStruct;
    using MyClassAA = MyClassA;

    using ByteSeqA = ByteSeq;
    using StringListA = StringList;
    using MyEnumDictA = MyEnumDict;
    dictionary<MyEnumA, MyEnumA> MyEnumDict2;

    interface AliasOperations
    {
        bool opBool(bool p1);
        boolA opBoolA(boolA p1);

        byte opByte(byte p1);
        byteA opByteA(byteA p1);

        short opShort(short p1);
        shortA opShortA(shortA p1);

        ushort opUShort(ushort p1);
        ushortA opUShortA(ushortA p1);

        int opInt(int p1);
        intA opIntA(intA p1);

        uint opUInt(uint p1);
        uintA opUIntA(uintA p1);

        varint opVarint(varint p1);
        varintA opVarintA(varintA p1);

        varuint opVaruint(varuint p1);
        varuintA opVaruintA(varuintA p1);

        long opLong(long p1);
        longA opLongA(longA p1);

        ulong opULong(ulong p1);
        ulongA opULongA(ulongA p1);

        varlong opVarlong(varlong p1);
        varlongA opVarlongA(varlongA p1);

        varulong opVarulong(varulong p1);
        varulongA opVarulongA(varulongA p1);

        float opFloat(float p1);
        floatA opFloatA(floatA p1);

        double opDouble(double p1);
        doubleA opDoubleA(doubleA p1);

        string opString(string p1);
        stringA opStringA(stringA p1);

        MyEnum opMyEnum(MyEnum p1);
        MyEnumA opMyEnumA(MyEnumA p1);

        MyStruct opMyStruct(MyStruct p1);
        MyStructA opMyStructA(MyStructA p1);

        MyClassA opMyClassA(MyClassA p1);
        MyClassAA opMyClassAA(MyClassAA p1);

        ByteSeq opByteSeq(ByteSeq p1);
        ByteSeqA opByteSeqA(ByteSeqA p1);

        StringList opStringList(StringList p1);
        StringListA opStringListA(StringListA p1);

        MyEnumDict opMyEnumDict(MyEnumDict p1);
        MyEnumDictA opMyEnumDictA(MyEnumDictA p1);

        MyEnumDict opMyEnumDict2(MyEnumDict p1);
        MyEnumDict2 opMyEnumDict2A(MyEnumDict2 p1);
    }

    module Alias
    {
        class MyClass
        {
            bool m1;
            byte m2;
            short m3;
            ushort m4;
            int m5;
            uint m6;
            varint m7;
            varuint m8;
            long m9;
            ulong m10;
            varlong m11;
            varulong m12;
            float m13;
            double m14;
            string m15;
            MyEnum m16;
            IceRpc::Tests::CodeGeneration::MyStruct m17;
            IceRpc::Tests::CodeGeneration::MyClassA m18;
            ByteSeq m19;
            MyEnumDict m20;
        }

        class MyClassA
        {
            boolA m1;
            byteA m2;
            shortA m3;
            ushortA m4;
            intA m5;
            uintA m6;
            varintA m7;
            varuintA m8;
            longA m9;
            ulongA m10;
            varlongA m11;
            varulongA m12;
            floatA m13;
            doubleA m14;
            stringA m15;
            MyEnumA m16;
            IceRpc::Tests::CodeGeneration::MyStructA m17;
            IceRpc::Tests::CodeGeneration::MyClassAA m18;
            ByteSeqA m19;
            MyEnumDictA m20;
        }

        class MyStruct
        {
            bool m1;
            byte m2;
            short m3;
            ushort m4;
            int m5;
            uint m6;
            varint m7;
            varuint m8;
            long m9;
            ulong m10;
            varlong m11;
            varulong m12;
            float m13;
            double m14;
            string m15;
            MyEnum m16;
            IceRpc::Tests::CodeGeneration::MyStruct m17;
            IceRpc::Tests::CodeGeneration::MyClassA m18;
            ByteSeq m19;
            MyEnumDict m20;
        }

        class MyStructA
        {
            boolA m1;
            byteA m2;
            shortA m3;
            ushortA m4;
            intA m5;
            uintA m6;
            varintA m7;
            varuintA m8;
            longA m9;
            ulongA m10;
            varlongA m11;
            varulongA m12;
            floatA m13;
            doubleA m14;
            stringA m15;
            MyEnumA m16;
            IceRpc::Tests::CodeGeneration::MyStructA m17;
            IceRpc::Tests::CodeGeneration::MyClassAA m18;
            ByteSeqA m19;
            MyEnumDictA m20;
        }
    }
}
