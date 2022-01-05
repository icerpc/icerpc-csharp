// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::ReferencedAssemblies
{
    class ClassC : ClassB
    {
        cValue: string,
    }

    class CompactClassC(3) : CompactClassB
    {
        cValue: string,
    }

    exception ExceptionC : ExceptionB
    {
        cValue: string,
    }
}
