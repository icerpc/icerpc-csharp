// Copyright (c) ZeroC, Inc.

#pragma once

module Ice::Generator::Base::Tests::ReferencedAssemblies
{
    class ClassA
    {
        string aValue;
    }

    class CompactClassA(1)
    {
        string aValue;
    }

    exception ExceptionA
    {
        string aValue;
    }
}
