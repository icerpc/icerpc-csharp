// Copyright (c) ZeroC, Inc.

#pragma once

#include "../A/A.ice"

module Ice::CodeGen::Tests::ReferencedAssemblies
{
    class ClassC : ClassA
    {
        string cValue;
    }

    class CompactClassC(3) : CompactClassA
    {
        string cValue;
    }

    exception ExceptionC : ExceptionA
    {
        string cValue;
    }
}
