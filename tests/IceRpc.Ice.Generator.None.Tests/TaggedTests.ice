// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::None::Tests
{
    struct FixedSizeStruct
    {
        int x;
        int y;
    }

    struct VarSizeStruct
    {
        string s;
    }

    enum MyEnum { One, Two }

    sequence<byte> ByteSeq;
    sequence<int> IntSeq;
    sequence<IntSeq> NestedIntSeq;

    class ClassWithTaggedFields
    {
        optional(1) byte a;            // Uses F1 tag format
        optional(2) short b;           // Uses F2 tag format
        optional(3) int c;             // Uses F4 tag format
        optional(4) long d;            // Uses F8 tag format
        optional(5) FixedSizeStruct e; // Uses VSize tag format
        optional(6) VarSizeStruct f;   // Uses FSize tag format
        optional(7) MyEnum g;          // Uses Size tag format
        optional(8) ByteSeq h;         // Uses OptimizedVSize tag format
        optional(9) IntSeq i;          // Uses VSize tag format
        optional(10) NestedIntSeq j;   // Uses FSize tag format
        optional(11) string k;         // Uses OptimizedVSize tag format
    }

    class ClassWithoutTaggedFields {}
}
