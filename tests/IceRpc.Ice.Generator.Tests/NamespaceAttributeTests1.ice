// Copyright (c) ZeroC, Inc.

["cs:identifier:IceRpc.Ice.Generator.Tests.NamespaceAttribute.MappedNamespace"]
module WithNamespace
{
    struct S1
    {
        int i;
    }
}

module IceRpc::Ice::Generator::Tests
{
    interface NamespaceOperations
    {
        WithNamespace::S1 op1(WithNamespace::S1 p);
    }
}
