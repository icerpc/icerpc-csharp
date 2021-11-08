// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ReferencedAssemblies
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
