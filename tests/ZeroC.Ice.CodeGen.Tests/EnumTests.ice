// Copyright (c) ZeroC, Inc.

module ZeroC::Ice::CodeGen::Tests
{
    enum MySlice1Enum
    {
        Enum1,
        Enum2,
        Enum3,
    }

    ["cs:attribute:System.Flags"]
    enum MySlice1FlagsEnum
    {
        E0 = 1,
        E1 = 2,
        E2 = 4,
        E3 = 8,

        ["cs:attribute:System.ComponentModel.Description(\"Sixteen\")"]
        E4 = 16,
    }
}
