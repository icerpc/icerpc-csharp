// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Operations
{
    interface TestService
    {
        void opVoid();
        (byte r1, byte r2) opByte(byte p1, byte p2);
        (bool r1, bool r2) opBool(bool p1, bool p2);
        (long r1, short r2, int r3, long r4) opShortIntLong(short p1, int p2, long p3);
        (ulong r1, ushort r2, uint r3, ulong r4) opUShortUIntULong(ushort p1, uint p2, ulong p3);

        varint opVarInt(varint v);
        varuint opVarUInt(varuint v);

        varlong opVarLong(varlong v);
        varulong opVarULong(varulong v);

        short opShort(short value);
        string opString(string value);
    }
}
