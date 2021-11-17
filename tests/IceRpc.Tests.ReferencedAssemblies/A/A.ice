// Copyright (c) ZeroC, Inc. All rights reserved.

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
