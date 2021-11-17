// Copyright (c) ZeroC, Inc. All rights reserved.

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
