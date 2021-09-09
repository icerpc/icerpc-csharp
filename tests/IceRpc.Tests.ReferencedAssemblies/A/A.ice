// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ReferencedAssemblies
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
