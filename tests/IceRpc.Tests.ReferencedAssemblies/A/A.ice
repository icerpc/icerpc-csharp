// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::ReferencedAssemblies
{
    class ClassA
    {
        aValue: string,
    }

    class CompactClassA(1)
    {
        aValue: string,
    }

    exception ExceptionA
    {
        aValue: string,
    }
}
