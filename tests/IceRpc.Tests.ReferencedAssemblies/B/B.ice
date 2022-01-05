// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::ReferencedAssemblies
{
    class ClassB : ClassA
    {
        bValue: string,
    }

    class CompactClassB(2) : CompactClassA
    {
        bValue: string,
    }

    exception ExceptionB : ExceptionA
    {
        bValue: string,
    }
}
