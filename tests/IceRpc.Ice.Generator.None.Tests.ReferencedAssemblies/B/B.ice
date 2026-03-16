// Copyright (c) ZeroC, Inc.

#pragma once

#include "../A/A.ice"

module Ice::Generator::None::Tests::ReferencedAssemblies
{
    class ClassB : ClassA
    {
        string bValue;
    }

    class CompactClassB(2) : CompactClassA
    {
        string bValue;
    }

    exception ExceptionB : ExceptionA
    {
        string bValue;
    }
}
