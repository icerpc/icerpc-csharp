// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    exception SomeException {}

    interface Operations
    {
        // Builtin types
        opByte(p1: byte, p2: byte) -> (r1: byte, r2: byte);
        opBool(p1: bool, p2: bool) -> (r1: bool, r2: bool);
        opShort(p1: short, p2: short) -> (r1: short, r2: short);
        opUShort(p1: ushort, p2: ushort) -> (r1: ushort, r2: ushort);
        opInt(p1: int, p2: int) -> (r1: int, r2: int);
        opVarInt(p1: varint, p2: varint) -> (r1: varint, r2: varint);
        opUInt(p1: uint, p2: uint) -> (r1: uint, r2: uint);
        opVarUInt(p1: varuint, p2: varuint) -> (r1: varuint, r2: varuint);
        opLong(p1: long, p2: long) -> (r1: long, r2: long);
        opVarLong(p1: varlong, p2: varlong) -> (r1: varlong, r2: varlong);
        opULong(p1: ulong, p2: ulong) -> (r1: ulong, r2: ulong);
        opVarULong(p1: varulong, p2: varulong) -> (r1: varulong, r2: varulong);
        opFloat(p1: float, p2: float) -> (r1: float, r2: float);
        opDouble(p1: double, p2: double) -> (r1: double, r2: double);
        opString(p1: string, p2: string) -> (r1: string, r2: string);

        // Returns a proxy to a Service
        opService(service: IceRpc::Service) -> IceRpc::Service;

        // Oneway Operations
        opOneway();
        [oneway]
        opOnewayMetadata();
    }

    interface DerivedOperations : Operations
    {
    }

    interface NoOperations
    {
    }
}
