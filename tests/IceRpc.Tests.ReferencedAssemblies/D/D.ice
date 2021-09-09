// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <C.ice>

module IceRpc::Tests::ReferencedAssemblies
{
    class ClassD : ClassC
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
