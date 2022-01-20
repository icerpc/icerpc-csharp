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
        opBool(p1: bool) -> bool;
        opBoolA(p1: boolA) -> boolA;

        opByte(p1: byte) -> byte;
        opByteA(p1: byteA) -> byteA;

        opShort(p1: short) -> short;
        opShortA(p1: shortA) -> shortA;

        opUShort(p1: ushort) -> ushort;
        opUShortA(p1: ushortA) -> ushortA;

        opInt(p1: int) -> int;
        opIntA(p1: intA) -> intA;

        opUInt(p1: uint) -> uint;
        opUIntA(p1: uintA) -> uintA;

        opVarint(p1: varint) -> varint;
        opVarintA(p1: varintA) -> varintA;

        opVaruint(p1: varuint) -> varuint;
        opVaruintA(p1: varuintA) -> varuintA;

        opLong(p1: long) -> long;
        opLongA(p1: longA) -> longA;

        opULong(p1: ulong) -> ulong;
        opULongA(p1: ulongA) -> ulongA;

        opVarlong(p1: varlong) -> varlong;
        opVarlongA(p1: varlongA) -> varlongA;

        opVarulong(p1: varulong) -> varulong;
        opVarulongA(p1: varulongA) -> varulongA;

        opFloat(p1: float) -> float;
        opFloatA(p1: floatA) -> floatA;

        opDouble(p1: double) -> double;
        opDoubleA(p1: doubleA) -> doubleA;

        opString(p1: string) -> string;
        opStringA(p1: stringA) -> stringA;

        opMyEnum(p1: MyEnum) -> MyEnum;
        opMyEnumA(p1: MyEnumA) -> MyEnumA;

        opMyStruct(p1: MyStruct) -> MyStruct;
        opMyStructA(p1: MyStructA) -> MyStructA;

        opMyClassA(p1: MyClassA) -> MyClassA;
        opMyClassAA(p1: MyClassAA) -> MyClassAA;

        opByteSeq(p1: ByteSeq) -> ByteSeq;
        opByteSeqA(p1: ByteSeqA) -> ByteSeqA;

        opStringList(p1: StringList) -> StringList;
        opStringListA(p1: StringListA) -> StringListA;

        opMyEnumDict(p1: MyEnumDict) -> MyEnumDict;
        opMyEnumDictA(p1: MyEnumDictA) -> MyEnumDictA;

        opMyEnumDict2(p1: MyEnumDict) -> MyEnumDict;
        opMyEnumDict2A(p1: MyEnumDict2) -> MyEnumDict2;
    }

    module Alias
    {
        class MyClass
        {
            m1: bool,
            m2: byte,
            m3: short,
            m4: ushort,
            m5: int,
            m6: uint,
            m7: varint,
            m8: varuint,
            m9: long,
            m10: ulong,
            m11: varlong,
            m12: varulong,
            m13: float,
            m14: double,
            m15: string,
            m16: MyEnum,
            m17: IceRpc::Tests::Slice::MyStruct,
            m18: IceRpc::Tests::Slice::MyClassA,
            m19: ByteSeq,
            m20: MyEnumDict,
        }

        class MyClassA
        {
            m1: boolA,
            m2: byteA,
            m3: shortA,
            m4: ushortA,
            m5: intA,
            m6: uintA,
            m7: varintA,
            m8: varuintA,
            m9: longA,
            m10: ulongA,
            m11: varlongA,
            m12: varulongA,
            m13: floatA,
            m14: doubleA,
            m15: stringA,
            m16: MyEnumA,
            m17: IceRpc::Tests::Slice::MyStructA,
            m18: IceRpc::Tests::Slice::MyClassAA,
            m19: ByteSeqA,
            m20: MyEnumDictA,
        }

        class MyStruct
        {
            m1: bool,
            m2: byte,
            m3: short,
            m4: ushort,
            m5: int,
            m6: uint,
            m7: varint,
            m8: varuint,
            m9: long,
            m10: ulong,
            m11: varlong,
            m12: varulong,
            m13: float,
            m14: double,
            m15: string,
            m16: MyEnum,
            m17: IceRpc::Tests::Slice::MyStruct,
            m18: IceRpc::Tests::Slice::MyClassA,
            m19: ByteSeq,
            m20: MyEnumDict,
        }

        class MyStructA
        {
            m1: boolA,
            m2: byteA,
            m3: shortA,
            m4: ushortA,
            m5: intA,
            m6: uintA,
            m7: varintA,
            m8: varuintA,
            m9: longA,
            m10: ulongA,
            m11: varlongA,
            m12: varulongA,
            m13: floatA,
            m14: doubleA,
            m15: stringA,
            m16: MyEnumA,
            m17: IceRpc::Tests::Slice::MyStructA,
            m18: IceRpc::Tests::Slice::MyClassAA,
            m19: ByteSeqA,
            m20: MyEnumDictA,
        }
    }
}
