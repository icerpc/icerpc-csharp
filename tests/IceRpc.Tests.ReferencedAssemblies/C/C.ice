// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <B.ice>

module IceRpc::Tests::ReferencedAssemblies
{
    class ClassC : ClassB
    {
        string cValue;
    }

    class CompactClassC(3) : CompactClassB
    {
        string cValue;
    }

    exception ExceptionC : ExceptionB
    {
        string cValue;
    }
}
