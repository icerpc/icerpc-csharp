// Copyright (c) ZeroC, Inc.

#pragma once

#include "../B/B.ice"
#include "../C/C.ice"

module Slice::IceGen::Tests::ReferencedAssemblies
{
    class ClassD : ClassB
    {
        string dValue;
    }

    class CompactClassD(4) : CompactClassC
    {
        string dValue;
    }

    exception ExceptionD : ExceptionC
    {
        string dValue;
    }
}
