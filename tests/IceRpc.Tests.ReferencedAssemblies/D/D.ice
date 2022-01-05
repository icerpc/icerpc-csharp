// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::ReferencedAssemblies
{
    class ClassD : ClassC
    {
        dValue: string,
    }

    class CompactClassD(4) : CompactClassC
    {
        dValue: string,
    }

    exception ExceptionD : ExceptionC
    {
        dValue: string,
    }
}
