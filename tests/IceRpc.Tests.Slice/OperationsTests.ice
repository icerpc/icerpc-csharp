// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once
#include <IceRpc/Service.ice>

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Slice
{
    exception SomeException {}

    interface Operations
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

        // Returns a proxy to a Service
        IceRpc::Service opService(IceRpc::Service service);

        // Oneway Operations
        void opOneway();
        [oneway]
        void opOnewayMetadata();
    }

    interface DerivedOperations : Operations
    {
    }
}
