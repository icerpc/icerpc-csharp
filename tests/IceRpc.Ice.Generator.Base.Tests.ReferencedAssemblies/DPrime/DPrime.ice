// Copyright (c) ZeroC, Inc.

#pragma once

#include "../B/B.ice"
#include "../C/C.ice"

module Ice::Generator::Base::Tests::ReferencedAssemblies
{
    // Exactly the same definitions as D but in a different assembly and with an extra suffix.
    // The type IDs remain the same.

    ["cs:identifier:ClassDPrime"]
    class ClassD : ClassB
    {
        string dValue;
    }

    ["cs:identifier:CompactClassDPrime"]
    class CompactClassD(4) : CompactClassC
    {
        string dValue;
    }

    ["cs:identifier:ExceptionDPrime"]
    exception ExceptionD : ExceptionC
    {
        string dValue;
    }
}
