// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    // builtin types
    typealias boolA = bool;
    typealias byteA = byte;
    typealias shortA = short;
    typealias ushortA = ushort;
    typealias intA = int;
    typealias uintA = uint;
    typealias varintA = varint;
    typealias varuintA = varuint;
    typealias longA = long;
    typealias ulongA = ulong;
    typealias varlongA = varlong;
    typealias varulongA = varulong;
    typealias floatA = float;
    typealias doubleA = double;
    typealias stringA = string;

    typealias MyEnumA = MyEnum;
    typealias MyStructA = MyStruct;
    typealias MyClassAA = MyClassA;

    typealias ByteSeqA = ByteSeq;
    typealias StringListA = StringList;
    typealias MyEnumDictA = MyEnumDict;
    typealias MyEnumDict2 = dictionary<MyEnumA, MyEnumA>;

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
            IceRpc::Tests::Slice::MyStruct m17;
            IceRpc::Tests::Slice::MyClassA m18;
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
            IceRpc::Tests::Slice::MyStructA m17;
            IceRpc::Tests::Slice::MyClassAA m18;
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
            IceRpc::Tests::Slice::MyStruct m17;
            IceRpc::Tests::Slice::MyClassA m18;
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
            IceRpc::Tests::Slice::MyStructA m17;
            IceRpc::Tests::Slice::MyClassAA m18;
            ByteSeqA m19;
            MyEnumDictA m20;
        }
    }
}
